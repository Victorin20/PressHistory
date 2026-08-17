using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using PressHistory.Models;
using PressHistory.Services;
using PressHistory.ViewModels;

namespace PressHistory;

public partial class App : System.Windows.Application
{
    private SingleInstanceService? _singleInstanceService;
    private ClipboardMonitor? _clipboardMonitor;
    private TrayService? _trayService;
    private HistoryService? _historyService;
    private SettingsStore? _settingsStore;
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;
    private AppSettings? _settings;
    private bool _isExiting;
    private bool _showRequestedDuringStartup;

    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _singleInstanceService = new SingleInstanceService();
        if (!_singleInstanceService.IsPrimaryInstance)
        {
            await SingleInstanceService.SignalPrimaryInstanceAsync();
            _singleInstanceService.Dispose();
            Shutdown();
            return;
        }

        _singleInstanceService.StartListening(() =>
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (_mainWindow is null)
                {
                    _showRequestedDuringStartup = true;
                }
                else
                {
                    ShowMainWindow();
                }
            }));

        try
        {
            var paths = new AppPaths();
            _settingsStore = new SettingsStore(paths);
            _settings = await _settingsStore.LoadAsync();

            _historyService = new HistoryService(
                new HistoryStore(paths),
                _settings.MaxEntries);
            await _historyService.LoadAsync();

            var startupManager = new StartupManager();
            var clipboardService = new ClipboardService(Dispatcher);
            _viewModel = new MainViewModel(
                _historyService,
                clipboardService,
                _settingsStore,
                startupManager,
                _settings,
                Dispatcher);

            _mainWindow = new MainWindow(_viewModel);
            MainWindow = _mainWindow;
            _mainWindow.CloseToTrayRequested += OnCloseToTrayRequested;

            var windowHandle = new WindowInteropHelper(_mainWindow).EnsureHandle();
            _clipboardMonitor = new ClipboardMonitor();
            _clipboardMonitor.Initialize(windowHandle);
            _clipboardMonitor.ClipboardUpdated += (_, sequence) =>
                _ = Dispatcher.BeginInvoke(
                    () => _viewModel.QueueClipboardCapture(sequence),
                    DispatcherPriority.Background);
            _clipboardMonitor.ShowRequested += (_, _) =>
                _ = Dispatcher.BeginInvoke(ShowMainWindow, DispatcherPriority.Normal);
            _viewModel.HotkeyAvailable = _clipboardMonitor.IsHotkeyRegistered;

            _trayService = new TrayService();
            WireTrayEvents();
            UpdateTrayState();
            _viewModel.StateChanged += (_, _) => UpdateTrayState();

            var startInBackground = eventArgs.Args.Any(argument =>
                string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
            if (!startInBackground || _showRequestedDuringStartup)
            {
                ShowMainWindow();
            }

        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"PressHistory n’a pas pu démarrer.\n\n{exception.Message}",
                "Erreur de démarrage",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            DisposeResources();
            Shutdown(-1);
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs eventArgs)
    {
        try
        {
            _clipboardMonitor?.Dispose();
            _viewModel?.StopCapture();
            _viewModel?.FlushAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Windows is ending the session; do not delay shutdown with an error dialog.
        }

        base.OnSessionEnding(eventArgs);
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        DisposeResources();
        base.OnExit(eventArgs);
    }

    private void WireTrayEvents()
    {
        if (_trayService is null)
        {
            return;
        }

        _trayService.OpenRequested += (_, _) =>
            Dispatcher.BeginInvoke(ShowMainWindow);
        _trayService.CaptureToggleRequested += (_, _) =>
            Dispatcher.BeginInvoke(() => _viewModel?.ToggleCapture());
        _trayService.StartupToggleRequested += (_, _) =>
            Dispatcher.BeginInvoke(() => _viewModel?.ToggleStartup());
        _trayService.ClearRequested += (_, _) =>
            Dispatcher.BeginInvoke(async () =>
            {
                ShowMainWindow();
                if (_mainWindow is not null)
                {
                    await _mainWindow.ConfirmAndClearHistoryAsync();
                }
            });
        _trayService.ExitRequested += (_, _) =>
            Dispatcher.BeginInvoke(async () => await ExitApplicationAsync());
    }

    private void ShowMainWindow()
    {
        if (_isExiting)
        {
            return;
        }

        _mainWindow?.ShowAndActivate();
    }

    private async Task ExitApplicationAsync()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _clipboardMonitor?.Dispose();

        try
        {
            if (_viewModel is not null)
            {
                await _viewModel.StopCaptureAsync();
                await _viewModel.FlushAsync();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"L’historique n’a pas pu être entièrement sauvegardé.\n\n{exception.Message}",
                "PressHistory",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        if (_mainWindow is not null)
        {
            _mainWindow.AllowClose = true;
            _mainWindow.Close();
        }

        DisposeResources();
        Shutdown();
    }

    private void OnCloseToTrayRequested(object? sender, EventArgs eventArgs)
    {
        if (_settings is null || _settingsStore is null || _settings.HasShownTrayHint)
        {
            return;
        }

        _settings.HasShownTrayHint = true;
        _trayService?.ShowStillRunningHint();
        _ = SaveSettingsQuietlyAsync();
    }

    private async Task SaveSettingsQuietlyAsync()
    {
        try
        {
            if (_settings is not null && _settingsStore is not null)
            {
                await _settingsStore.SaveAsync(_settings.Snapshot());
            }
        }
        catch
        {
            // This hint is cosmetic; a save failure is already handled during normal exit.
        }
    }

    private void UpdateTrayState()
    {
        if (_trayService is null || _viewModel is null)
        {
            return;
        }

        _trayService.UpdateState(
            _viewModel.CaptureEnabled,
            _viewModel.StartupEnabled);
    }

    private void DisposeResources()
    {
        _clipboardMonitor?.Dispose();
        _clipboardMonitor = null;

        _trayService?.Dispose();
        _trayService = null;

        _viewModel?.Dispose();
        _viewModel = null;

        _historyService?.Dispose();
        _historyService = null;

        _singleInstanceService?.Dispose();
        _singleInstanceService = null;
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        MessageBox.Show(
            "Une erreur inattendue est survenue. PressHistory va se fermer pour protéger l’historique local.",
            "PressHistory",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        _ = ExitApplicationAsync();
    }
}
