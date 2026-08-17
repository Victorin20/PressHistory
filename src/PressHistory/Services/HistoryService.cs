using System.Collections.ObjectModel;
using System.Text;
using PressHistory.Models;

namespace PressHistory.Services;

public sealed class HistoryService : IDisposable
{
    public const long MaximumHistoryBytes = 32L * 1024 * 1024;

    private readonly HistoryStore _store;
    private CancellationTokenSource? _saveDelayCancellation;
    private Task _pendingSave = Task.CompletedTask;
    private int _maxEntries;
    private readonly long _maximumBytes;
    private bool _disposed;

    public HistoryService(
        HistoryStore store,
        int maxEntries = 250,
        long maximumBytes = MaximumHistoryBytes)
    {
        _store = store;
        _maxEntries = NormalizeLimit(maxEntries);
        _maximumBytes = Math.Clamp(maximumBytes, 1_024, 256L * 1024 * 1024);
    }

    public ObservableCollection<ClipboardEntry> Entries { get; } = [];

    public event EventHandler? Changed;

    public event EventHandler<Exception>? PersistenceFailed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var loadedEntries = await _store.LoadAsync(cancellationToken);
        var uniqueEntries = new List<ClipboardEntry>();

        foreach (var entry in loadedEntries
                     .Where(entry => !string.IsNullOrWhiteSpace(entry.Text))
                     .OrderByDescending(entry => entry.CapturedAtUtc))
        {
            entry.Hash = string.IsNullOrWhiteSpace(entry.Hash)
                ? ClipboardTextHasher.Compute(entry.Text)
                : entry.Hash;

            if (uniqueEntries.Any(existing =>
                    existing.Hash == entry.Hash &&
                    string.Equals(existing.Text, entry.Text, StringComparison.Ordinal)))
            {
                continue;
            }

            uniqueEntries.Add(entry);

            if (uniqueEntries.Count >= _maxEntries)
            {
                break;
            }
        }

        Entries.Clear();
        foreach (var entry in uniqueEntries)
        {
            Entries.Add(entry);
        }

        TrimToLimit();

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ClipboardEntry? AddOrPromote(string text, DateTimeOffset? capturedAtUtc = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var hash = ClipboardTextHasher.Compute(text);
        var existing = Entries.FirstOrDefault(entry =>
            entry.Hash == hash &&
            string.Equals(entry.Text, text, StringComparison.Ordinal));
        var timestamp = capturedAtUtc ?? DateTimeOffset.UtcNow;

        if (existing is not null)
        {
            existing.Touch(timestamp);
            var currentIndex = Entries.IndexOf(existing);
            if (currentIndex > 0)
            {
                Entries.Move(currentIndex, 0);
            }

            NotifyChanged();
            return existing;
        }

        var entry = new ClipboardEntry
        {
            Id = Guid.NewGuid(),
            Text = text,
            Hash = hash,
            CapturedAtUtc = timestamp
        };

        var insertionIndex = 0;
        while (insertionIndex < Entries.Count &&
               Entries[insertionIndex].CapturedAtUtc > timestamp)
        {
            insertionIndex++;
        }

        Entries.Insert(insertionIndex, entry);
        TrimToLimit();
        NotifyChanged();
        return entry;
    }

    public bool Remove(ClipboardEntry? entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (entry is null || !Entries.Remove(entry))
        {
            return false;
        }

        NotifyChanged();
        return true;
    }

    public bool Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Entries.Count == 0)
        {
            return false;
        }

        Entries.Clear();
        NotifyChanged();
        return true;
    }

    public void SetMaximumEntries(int maximumEntries)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _maxEntries = NormalizeLimit(maximumEntries);
        var removedAny = TrimToLimit();
        if (removedAny)
        {
            NotifyChanged();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        _saveDelayCancellation?.Cancel();
        var snapshot = CreateSnapshot();
        await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _saveDelayCancellation?.Cancel();
        _saveDelayCancellation?.Dispose();
    }

    private void NotifyChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        ScheduleSave();
    }

    private bool TrimToLimit()
    {
        var removedAny = false;
        var totalBytes = Entries.Sum(EstimateStoredBytes);

        while (Entries.Count > _maxEntries || totalBytes > _maximumBytes)
        {
            var lastEntry = Entries[^1];
            totalBytes -= EstimateStoredBytes(lastEntry);
            Entries.RemoveAt(Entries.Count - 1);
            removedAny = true;
        }

        return removedAny;
    }

    private void ScheduleSave()
    {
        _saveDelayCancellation?.Cancel();
        _saveDelayCancellation?.Dispose();
        _saveDelayCancellation = new CancellationTokenSource();
        var token = _saveDelayCancellation.Token;
        var snapshot = CreateSnapshot();
        _pendingSave = SaveAfterDelayAsync(snapshot, token);
    }

    private async Task SaveAfterDelayAsync(
        IReadOnlyCollection<ClipboardEntry> snapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
            await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer snapshot superseded this one.
        }
        catch (Exception exception)
        {
            PersistenceFailed?.Invoke(this, exception);
        }
    }

    private IReadOnlyCollection<ClipboardEntry> CreateSnapshot()
    {
        return Entries.Select(entry => entry.Snapshot()).ToArray();
    }

    private static int NormalizeLimit(int value) => Math.Clamp(value, 20, 1_000);

    private static long EstimateStoredBytes(ClipboardEntry entry)
    {
        return Encoding.UTF8.GetByteCount(entry.Text) + 128L;
    }
}
