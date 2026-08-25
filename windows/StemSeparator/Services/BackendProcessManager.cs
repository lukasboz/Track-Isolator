using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using StemSeparator.Utilities;

namespace StemSeparator.Services;

public class BackendProcessManager : IDisposable
{
    private Process? _backendProcess;
    private bool _disposed;

    public int Port { get; private set; }
    public bool IsHealthy { get; private set; }

    public async Task StartAsync()
    {
        // First check if a backend is already running on the default port
        if (await TryExistingBackendAsync(Constants.DefaultPort))
        {
            Port = Constants.DefaultPort;
            IsHealthy = true;
            _managedExternally = true;
            Debug.WriteLine(
                $"[BackendProcessManager] Found existing backend on port {Port}.");
            return;
        }

        Port = FindFreePort(Constants.DefaultPort);

        var startInfo = ResolveLaunchConfig();
        if (startInfo is null)
            throw new FileNotFoundException(
                "Backend executable not found and no Python installation available. " +
                "Either build the PyInstaller bundle or install Python and the backend dependencies.");

        startInfo.EnvironmentVariables["PORT"] = Port.ToString();
        startInfo.EnvironmentVariables["HOST"] = "127.0.0.1";

        // Replace port placeholder in Arguments (used for Python fallback)
        if (startInfo.Arguments.Contains("{PORT}"))
            startInfo.Arguments = startInfo.Arguments.Replace("{PORT}", Port.ToString());

        _backendProcess = new Process { StartInfo = startInfo };

        _backendProcess.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Debug.WriteLine($"[Backend stdout] {e.Data}");
        };

        _backendProcess.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Debug.WriteLine($"[Backend stderr] {e.Data}");
        };

        _backendProcess.Start();
        _backendProcess.BeginOutputReadLine();
        _backendProcess.BeginErrorReadLine();

        await WaitForHealthAsync();
    }

    private bool _managedExternally;

    public void Stop()
    {
        if (_managedExternally || _backendProcess is null || _backendProcess.HasExited)
            return;

        try
        {
            _backendProcess.Kill(entireProcessTree: true);
            _backendProcess.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BackendProcessManager] Error stopping process: {ex.Message}");
        }
        finally
        {
            _backendProcess.Dispose();
            _backendProcess = null;
            IsHealthy = false;
        }
    }

    private static async Task<bool> TryExistingBackendAsync(int port)
    {
        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri(Constants.GetBaseUrl(port)),
                Timeout = TimeSpan.FromSeconds(2)
            };
            using var response = await client.GetAsync("/health");
            if (response.StatusCode != HttpStatusCode.OK)
                return false;

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var health = await JsonDocument.ParseAsync(stream);
            return
                health.RootElement.TryGetProperty("status", out var status) &&
                status.GetString() == "healthy" &&
                health.RootElement.TryGetProperty("service", out var service) &&
                service.GetString() == "stem-separator-local";
        }
        catch
        {
            return false;
        }
    }

    private static ProcessStartInfo? ResolveLaunchConfig()
    {
        // 1. Production path: bundled PyInstaller exe alongside the app
        var productionPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            Constants.BackendSubfolder,
            Constants.BackendExecutableName);

        if (File.Exists(productionPath))
        {
            var si = MakeStartInfo(productionPath);
            // Look for bundled ffmpeg
            var ffmpeg = Path.Combine(Path.GetDirectoryName(productionPath)!, "ffmpeg.exe");
            if (File.Exists(ffmpeg))
                si.EnvironmentVariables["FFMPEG_PATH"] = Path.GetDirectoryName(ffmpeg);
            return si;
        }

        // Locate the repository by walking upward instead of assuming a fixed
        // bin/Debug target depth, which varies between IDE and publish builds.
        var projectRoot = FindProjectRoot();
        if (projectRoot is null)
            return null;

        // 2. Dev path: PyInstaller dist
        var devExePath = Path.Combine(
            projectRoot,
            "backend", "pyinstaller", "dist",
            "stem-separator-backend",
            Constants.BackendExecutableName);

        if (File.Exists(devExePath))
            return MakeStartInfo(devExePath);

        // 3. Development fallback: run the shared local backend directly.
        var newBackendApp = Path.Combine(projectRoot, "backend", "app.py");
        if (!File.Exists(newBackendApp))
            return null;
        var workingDir = Path.GetDirectoryName(newBackendApp)!;

        // Try venv python first, then system python
        var venvPython = Path.Combine(workingDir, "venv", "Scripts", "python.exe");
        var pythonExe = File.Exists(venvPython) ? venvPython : "python";

        Debug.WriteLine(
            $"[BackendProcessManager] Using Python: {pythonExe}, script: app.py, cwd: {workingDir}");

        return new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"app.py --host 127.0.0.1 --port {{PORT}}",
            WorkingDirectory = workingDir,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
    }

    private static string? FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "backend", "app.py")))
                return directory.FullName;
            directory = directory.Parent;
        }

        return null;
    }

    private static ProcessStartInfo MakeStartInfo(string exePath)
    {
        return new ProcessStartInfo
        {
            FileName = exePath,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
    }

    private static int FindFreePort(int startPort)
    {
        for (int port = startPort; port < startPort + 100; port++)
        {
            if (IsPortAvailable(port))
                return port;
        }

        throw new InvalidOperationException(
            $"Could not find a free port in range {startPort}-{startPort + 99}.");
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private async Task WaitForHealthAsync()
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(Constants.GetBaseUrl(Port)),
            Timeout = TimeSpan.FromSeconds(5)
        };

        var elapsed = 0;

        while (elapsed < Constants.HealthCheckTimeoutMs)
        {
            // If the process exited before becoming healthy, bail out early
            if (_backendProcess is null || _backendProcess.HasExited)
                throw new InvalidOperationException(
                    $"Backend process exited before becoming healthy " +
                    $"(exit code {_backendProcess?.ExitCode}).");

            try
            {
                var response = await httpClient.GetAsync("/health");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    IsHealthy = true;
                    Debug.WriteLine(
                        $"[BackendProcessManager] Backend healthy on port {Port} " +
                        $"after {elapsed}ms.");
                    return;
                }
            }
            catch (Exception)
            {
                // Server not ready yet — ignore and retry
            }

            await Task.Delay(Constants.HealthCheckIntervalMs);
            elapsed += Constants.HealthCheckIntervalMs;
        }

        throw new TimeoutException(
            $"Backend did not become healthy within " +
            $"{Constants.HealthCheckTimeoutMs / 1000} seconds on port {Port}.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
