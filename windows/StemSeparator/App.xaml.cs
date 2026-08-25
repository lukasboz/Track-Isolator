using System.Windows;
using System.Windows.Threading;
using StemSeparator.Models;
using StemSeparator.ViewModels;

namespace StemSeparator;

public partial class App : Application
{
    internal static MainViewModel MainViewModel { get; } = new();

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        // Catch unhandled exceptions so the app doesn't just vanish
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        await MainViewModel.InitializeAsync();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MainViewModel.CurrentState = AppState.Error;
        MainViewModel.ErrorMessage = $"Unexpected error: {e.Exception.Message}";
        e.Handled = true;  // Prevent app from crashing
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Dispatcher.BeginInvoke(() =>
            {
                MainViewModel.CurrentState = AppState.Error;
                MainViewModel.ErrorMessage = $"Fatal error: {ex.Message}";
            });
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            MainViewModel.CurrentState = AppState.Error;
            MainViewModel.ErrorMessage = $"Background error: {e.Exception.InnerException?.Message ?? e.Exception.Message}";
        });
        e.SetObserved();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        MainViewModel.Shutdown();
    }
}
