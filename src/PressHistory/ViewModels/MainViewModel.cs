using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using PressHistory.Infrastructure;
using PressHistory.Models;
using PressHistory.Services;

namespace PressHistory.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly HistoryService _historyService;
    private readonly ClipboardService _clipboardService;
    private readonly SettingsStore _settingsStore;
    private readonly StartupManager _startupManager;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timeRefreshTimer;
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _captureCancellation = new();
    private readonly AsyncRelayCommand<ClipboardEntry> _copyCommand;
    private readonly RelayCommand<ClipboardEntry> _deleteCommand;

    private string _searchText = string.Empty;
    private bool _captureEnabled;
    private bool _startupEnabled;
    private bool _captureLoopRunning;
    private bool _captureStopRequested;
    private Task _captureTask = Task.CompletedTask;
    private uint _pendingClipboardSequence;
    private ClipboardEntry? _selectedEntry;
    private string _statusMessage = string.Empty;
    private bool _statusIsError;
    private bool _hotkeyAvailable;
    private CancellationTokenSource? _statusCancellation;
    private bool _disposed;

    public MainViewModel(
        HistoryService historyService,
        ClipboardService clipboardService,
        SettingsStore settingsStore,
        StartupManager startupManager,
        AppSettings settings,
        Dispatcher dispatcher)
    {
        _historyService = historyService;
        _clipboardService = clipboardService;
        _settingsStore = settingsStore;
        _startupManager = startupManager;
        _settings = settings;
        _dispatcher = dispatcher;
        _captureEnabled = settings.CaptureEnabled;

        try
        {
            _startupEnabled = startupManager.IsEnabled;
        }
        catch
        {
            _startupEnabled = false;
        }

        EntriesView = CollectionViewSource.GetDefaultView(_historyService.Entries);
        EntriesView.Filter = FilterEntry;

        _copyCommand = new AsyncRelayCommand<ClipboardEntry>(
            entry => CopyEntryAsync(entry, hideAfterCopy: false),
            entry => entry is not null);
        _deleteCommand = new RelayCommand<ClipboardEntry>(
            DeleteEntry,
            entry => entry is not null);

        _historyService.Changed += OnHistoryChanged;
        _historyService.PersistenceFailed += OnPersistenceFailed;
        _historyService.Entries.CollectionChanged += OnEntriesCollectionChanged;

        _timeRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _timeRefreshTimer.Tick += (_, _) => RefreshTimeLabels();
        _timeRefreshTimer.Start();
    }

    public ICollectionView EntriesView { get; }

    public ICommand CopyCommand => _copyCommand;

    public ICommand DeleteCommand => _deleteCommand;

    public ClipboardEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (SetProperty(ref _selectedEntry, value))
            {
                _copyCommand.RaiseCanExecuteChanged();
                _deleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value ?? string.Empty))
            {
                return;
            }

            EntriesView.Refresh();
            NotifyListPresentationChanged();
        }
    }

    public bool CaptureEnabled
    {
        get => _captureEnabled;
        set
        {
            if (!SetProperty(ref _captureEnabled, value))
            {
                return;
            }

            _settings.CaptureEnabled = value;
            if (!value)
            {
                _pendingClipboardSequence = 0;
            }

            OnPropertyChanged(nameof(CaptureButtonLabel));
            OnPropertyChanged(nameof(CaptureDescription));
            StateChanged?.Invoke(this, EventArgs.Empty);
            _ = PersistSettingsAsync();
            ShowStatus(value ? "Capture reprise" : "Capture suspendue");

        }
    }

    public bool StartupEnabled
    {
        get => _startupEnabled;
        set
        {
            if (_startupEnabled == value)
            {
                return;
            }

            try
            {
                _startupManager.SetEnabled(value);
                _startupEnabled = _startupManager.IsEnabled;
                OnPropertyChanged();
                StateChanged?.Invoke(this, EventArgs.Empty);
                ShowStatus(
                    _startupEnabled
                        ? "Démarrage avec Windows activé"
                        : "Démarrage avec Windows désactivé");
            }
            catch (Exception exception)
            {
                OnPropertyChanged();
                ShowStatus($"Démarrage Windows : {exception.Message}", isError: true);
            }
        }
    }

    public bool HotkeyAvailable
    {
        get => _hotkeyAvailable;
        set
        {
            if (SetProperty(ref _hotkeyAvailable, value))
            {
                OnPropertyChanged(nameof(ShortcutLabel));
            }
        }
    }

    public string CaptureButtonLabel => CaptureEnabled ? "Capture active" : "En pause";

    public string CaptureDescription => CaptureEnabled
        ? "Les nouveaux textes copiés sont enregistrés."
        : "Aucun nouveau contenu n’est enregistré.";

    public string ShortcutLabel => HotkeyAvailable
        ? "Ctrl + Alt + H pour afficher"
        : "Raccourci global indisponible";

    public string StorageLimitLabel =>
        $"●  Stockage local uniquement · {_settings.MaxEntries} éléments · 32 Mo maximum";

    public string HistorySummary
    {
        get
        {
            var visibleCount = GetVisibleCount();
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                return visibleCount switch
                {
                    0 => "Aucun résultat",
                    1 => "1 résultat",
                    _ => $"{visibleCount} résultats"
                };
            }

            return _historyService.Entries.Count switch
            {
                0 => "Aucun élément",
                1 => "1 élément enregistré",
                _ => $"{_historyService.Entries.Count} éléments enregistrés"
            };
        }
    }

    public bool HasVisibleEntries => GetVisibleCount() > 0;

    public bool HasEntries => _historyService.Entries.Count > 0;

    public string EmptyStateTitle => !HasEntries || string.IsNullOrWhiteSpace(SearchText)
        ? "Votre historique est vide"
        : "Aucun texte ne correspond";

    public string EmptyStateDescription => !HasEntries || string.IsNullOrWhiteSpace(SearchText)
        ? "Copiez du texte dans n’importe quelle application : il apparaîtra ici automatiquement."
        : "Essayez un autre mot ou effacez la recherche.";

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public event EventHandler? RequestHide;

    public event EventHandler? StateChanged;

    public void QueueClipboardCapture(uint sequenceNumber)
    {
        if (!CaptureEnabled || sequenceNumber == 0 || _disposed || _captureStopRequested)
        {
            return;
        }

        _pendingClipboardSequence = sequenceNumber;
        if (_captureLoopRunning)
        {
            return;
        }

        _captureLoopRunning = true;
        _captureTask = CapturePendingClipboardAsync(_captureCancellation.Token);
    }

    public Task CopySelectedAndHideAsync()
    {
        return SelectedEntry is null
            ? Task.CompletedTask
            : CopyEntryAsync(SelectedEntry, hideAfterCopy: true);
    }

    public void DeleteSelected()
    {
        DeleteEntry(SelectedEntry);
    }

    public async Task<bool> ClearHistoryAsync()
    {
        var cleared = _historyService.Clear();
        if (!cleared)
        {
            return false;
        }

        try
        {
            await _historyService.FlushAsync();
            ShowStatus("Historique effacé");
            return true;
        }
        catch (Exception exception)
        {
            ShowStatus($"Effacement incomplet : {exception.Message}", isError: true);
            return false;
        }
    }

    public void ToggleCapture() => CaptureEnabled = !CaptureEnabled;

    public void ToggleStartup() => StartupEnabled = !StartupEnabled;

    public void RefreshTimeLabels()
    {
        foreach (var entry in _historyService.Entries)
        {
            entry.RefreshTimeLabel();
        }
    }

    public void StopCapture()
    {
        if (_captureStopRequested)
        {
            return;
        }

        _captureStopRequested = true;
        _pendingClipboardSequence = 0;
        _captureCancellation.Cancel();
    }

    public async Task StopCaptureAsync()
    {
        StopCapture();

        try
        {
            await _captureTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when shutdown interrupts a clipboard retry delay.
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var settingsTask = _settingsStore.SaveAsync(_settings.Snapshot(), cancellationToken);
        var historyTask = _historyService.FlushAsync(cancellationToken);
        await Task.WhenAll(settingsTask, historyTask).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopCapture();
        _captureCancellation.Dispose();
        _timeRefreshTimer.Stop();
        _statusCancellation?.Cancel();
        _statusCancellation?.Dispose();
        _historyService.Changed -= OnHistoryChanged;
        _historyService.PersistenceFailed -= OnPersistenceFailed;
        _historyService.Entries.CollectionChanged -= OnEntriesCollectionChanged;
    }

    private async Task CapturePendingClipboardAsync(CancellationToken cancellationToken)
    {
        var staleRetries = 0;

        try
        {
            while (_pendingClipboardSequence != 0 &&
                   CaptureEnabled &&
                   !_disposed &&
                   !_captureStopRequested)
            {
                var sequence = _pendingClipboardSequence;
                _pendingClipboardSequence = 0;
                var result = await _clipboardService.ReadTextAsync(sequence, cancellationToken);

                if (!CaptureEnabled || _disposed)
                {
                    _pendingClipboardSequence = 0;
                    break;
                }

                switch (result.Status)
                {
                    case ClipboardReadStatus.Success when result.Text is not null:
                        _historyService.AddOrPromote(result.Text);
                        staleRetries = 0;
                        break;
                    case ClipboardReadStatus.Stale when
                        _pendingClipboardSequence == 0 &&
                        result.SequenceNumber != 0 &&
                        staleRetries < 2:
                        _pendingClipboardSequence = result.SequenceNumber;
                        staleRetries++;
                        break;
                    case ClipboardReadStatus.TooLarge:
                        ShowStatus("Texte ignoré : la limite est de 2 Mo", isError: true);
                        break;
                    case ClipboardReadStatus.Unavailable:
                        ShowStatus("Presse-papiers momentanément indisponible", isError: true);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown.
        }
        catch (Exception exception)
        {
            ShowStatus($"Capture impossible : {exception.Message}", isError: true);
        }
        finally
        {
            _captureLoopRunning = false;

            if (_pendingClipboardSequence != 0 &&
                CaptureEnabled &&
                !_disposed &&
                !_captureStopRequested)
            {
                QueueClipboardCapture(_pendingClipboardSequence);
            }
        }
    }

    private async Task CopyEntryAsync(ClipboardEntry? entry, bool hideAfterCopy)
    {
        if (entry is null)
        {
            return;
        }

        var copied = await _clipboardService.WriteTextAsync(entry.Text);
        if (!copied)
        {
            ShowStatus("Impossible d’accéder au presse-papiers", isError: true);
            return;
        }

        _historyService.AddOrPromote(entry.Text);
        SelectedEntry = entry;
        ShowStatus("Texte recopié dans le presse-papiers");

        if (hideAfterCopy)
        {
            RequestHide?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DeleteEntry(ClipboardEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (_historyService.Remove(entry))
        {
            if (ReferenceEquals(SelectedEntry, entry))
            {
                SelectedEntry = null;
            }

            ShowStatus("Élément supprimé");
        }
    }

    private bool FilterEntry(object item)
    {
        if (item is not ClipboardEntry entry || string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return CultureInfo.CurrentCulture.CompareInfo.IndexOf(
                   entry.Text,
                   SearchText.Trim(),
                   CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }

    private int GetVisibleCount()
    {
        return EntriesView.Cast<object>().Count();
    }

    private void OnHistoryChanged(object? sender, EventArgs eventArgs)
    {
        EntriesView.Refresh();
        NotifyListPresentationChanged();
    }

    private void OnEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        NotifyListPresentationChanged();
    }

    private void NotifyListPresentationChanged()
    {
        OnPropertyChanged(nameof(HistorySummary));
        OnPropertyChanged(nameof(HasVisibleEntries));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDescription));
    }

    private void OnPersistenceFailed(object? sender, Exception exception)
    {
        _dispatcher.BeginInvoke(() =>
            ShowStatus($"Sauvegarde impossible : {exception.Message}", isError: true));
    }

    private async Task PersistSettingsAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(_settings.Snapshot());
        }
        catch (Exception exception)
        {
            ShowStatus($"Paramètres non sauvegardés : {exception.Message}", isError: true);
        }
    }

    private async void ShowStatus(string message, bool isError = false)
    {
        _statusCancellation?.Cancel();
        _statusCancellation?.Dispose();
        _statusCancellation = new CancellationTokenSource();
        var cancellationToken = _statusCancellation.Token;

        StatusIsError = isError;
        StatusMessage = message;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(isError ? 6 : 3), cancellationToken);
            StatusMessage = string.Empty;
        }
        catch (OperationCanceledException)
        {
            // A newer status message replaced this one.
        }
    }
}
