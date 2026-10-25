using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using MultiStopwatch.Models;
using MultiStopwatch.ViewModels;

namespace MultiStopwatch;

public partial class App : Application
{
    // Only one copy may run: two copies would overwrite each other's saved state.
    private const string InstanceMutexName = @"Local\MultiStopwatch.SingleInstance";
    private const string ActivateEventName = @"Local\MultiStopwatch.Activate";
    private const int AsfwAny = -1;

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            ActivateFirstInstance();
            Shutdown();
            return;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        ThreadPool.RegisterWaitForSingleObject(_activateEvent, (_, _) => Dispatcher.BeginInvoke(BringToFront), null, Timeout.Infinite, executeOnlyOnce: false);

        base.OnStartup(e);
        ThemeMode = ThemeMode.System;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var (state, warning) = StateStore.Load();
        var window = new MainWindow(new MainViewModel(state ?? AppState.CreateDefault()));
        MainWindow = window;
        window.Show();

        if (warning != null)
            MessageBox.Show(window, warning, MainViewModel.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static void ActivateFirstInstance()
    {
        try
        {
            AllowSetForegroundWindow(AsfwAny);
            using var activate = EventWaitHandle.OpenExisting(ActivateEventName);
            activate.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    private void BringToFront()
    {
        if (MainWindow is not { } window)
            return;
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(MainWindow, $"Something went wrong:\n{e.Exception.Message}", MainViewModel.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
