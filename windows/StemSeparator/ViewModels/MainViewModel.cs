using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using StemSeparator.Models;
using StemSeparator.Services;
using StemSeparator.Utilities;

namespace StemSeparator.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private BackendProcessManager? _backendManager;
    private BackendApiClient? _apiClient;
    private SeparationResult? _activeResult;
    private CancellationTokenSource? _processingCancellation;

    [ObservableProperty]
    private AppState _currentState = AppState.Starting;

    [ObservableProperty]
    private string _progressText = "Starting backend engine...";

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _selectedFilePath;

    [ObservableProperty]
    private string? _selectedFileName;

    [ObservableProperty]
    private StemPlayerViewModel? _player;

    public async Task InitializeAsync()
    {
        try
        {
            CurrentState = AppState.Starting;
            ProgressText = "Starting backend engine...";

            _backendManager = new BackendProcessManager();
            await _backendManager.StartAsync();

            _apiClient = new BackendApiClient(_backendManager.Port);

            CurrentState = AppState.Idle;
            ProgressText = string.Empty;
        }
        catch (Exception ex)
        {
            CurrentState = AppState.Error;
            ErrorMessage = $"Failed to start backend: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SelectFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select an audio file",
            Filter = Constants.FileFilter,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            SelectPath(dialog.FileName);
        }
    }

    [RelayCommand]
    private void HandleFileDrop(string[] files)
    {
        if (files.Length == 1)
            SelectPath(files[0]);
    }

    [RelayCommand]
    private async Task ProcessAsync()
    {
        if (string.IsNullOrEmpty(SelectedFilePath) || _apiClient == null)
            return;

        var validationError = FileValidator.GetValidationError(SelectedFilePath);
        if (validationError is not null)
        {
            CurrentState = AppState.Error;
            ErrorMessage = validationError;
            return;
        }

        _processingCancellation?.Dispose();
        _processingCancellation = new CancellationTokenSource();

        try
        {
            ErrorMessage = null;
            CurrentState = AppState.Uploading;
            ProgressText = "Uploading file...";
            ProgressPercent = 0;

            var filePath = SelectedFilePath;
            var apiClient = _apiClient;

            // Uploading and SSE parsing are already asynchronous, so no extra
            // worker thread is needed for the duration of the separation.
            var result = await apiClient.SeparateAudioAsync(
                filePath,
                new Progress<SeparationProgress>(update =>
                {
                    ProgressText = update.Status;
                    if (update.Percent is int percent)
                        ProgressPercent = Math.Clamp(percent, 0, 100);
                    if (CurrentState == AppState.Uploading &&
                        update.Status.Contains("Separating", StringComparison.OrdinalIgnoreCase))
                    {
                        CurrentState = AppState.Processing;
                    }
                }),
                _processingCancellation.Token);

            _activeResult = result;

            // Create player (must be on UI thread)
            var player = new StemPlayerViewModel();
            player.LoadStems(result.StemPaths);
            Player = player;

            CurrentState = AppState.Ready;
            ProgressText = "Complete!";
            ProgressPercent = 100;
        }
        catch (OperationCanceledException)
        {
            CurrentState = AppState.Idle;
            ProgressText = "Separation canceled.";
            ProgressPercent = 0;
        }
        catch (Exception ex)
        {
            if (_activeResult is not null && _apiClient is not null)
            {
                await _apiClient.CleanupAsync(_activeResult);
                _activeResult = null;
            }
            CurrentState = AppState.Error;
            ErrorMessage = ex.Message;
        }
        finally
        {
            _processingCancellation?.Dispose();
            _processingCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelProcessing()
    {
        _processingCancellation?.Cancel();
    }

    [RelayCommand]
    private async Task ResetAsync()
    {
        // Stop playback
        Player?.Dispose();
        Player = null;

        // Clean up backend files
        if (_activeResult != null && _apiClient != null)
        {
            await _apiClient.CleanupAsync(_activeResult);
            _activeResult = null;
        }

        // Reset state
        SelectedFilePath = null;
        SelectedFileName = null;
        ErrorMessage = null;
        ProgressText = string.Empty;
        ProgressPercent = 0;
        CurrentState = AppState.Idle;
    }

    [RelayCommand]
    private void DismissError()
    {
        ErrorMessage = null;
        if (CurrentState == AppState.Error)
            CurrentState = AppState.Idle;
    }

    public void Shutdown()
    {
        _processingCancellation?.Cancel();
        Player?.Dispose();
        if (_activeResult is not null && _apiClient is not null)
        {
            using var cleanupTimeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(2));
            _apiClient
                .CleanupAsync(_activeResult, cleanupTimeout.Token)
                .GetAwaiter()
                .GetResult();
            _activeResult = null;
        }
        else
        {
            BackendApiClient.DeleteLocalFiles(_activeResult);
        }
        _apiClient?.Dispose();
        _backendManager?.Dispose();
    }

    private void SelectPath(string path)
    {
        var validationError = FileValidator.GetValidationError(path);
        if (validationError is not null)
        {
            CurrentState = AppState.Error;
            ErrorMessage = validationError;
            return;
        }

        ErrorMessage = null;
        SelectedFilePath = path;
        SelectedFileName = Path.GetFileName(path);
        CurrentState = AppState.Idle;
    }
}
