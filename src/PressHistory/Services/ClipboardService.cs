using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using PressHistory.Models;

namespace PressHistory.Services;

public sealed class ClipboardService
{
    public const int MaximumTextBytes = 2 * 1024 * 1024;

    private static readonly int[] RetryDelaysMilliseconds = [0, 25, 50, 100, 200, 400];
    private static readonly string[] ZeroMeansExcludedFormats =
    [
        "CanIncludeInClipboardHistory",
        "CanUploadToCloudClipboard"
    ];

    private readonly Dispatcher _dispatcher;

    public ClipboardService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task<ClipboardReadResult> ReadTextAsync(
        uint expectedSequenceNumber,
        CancellationToken cancellationToken = default)
    {
        EnsureDispatcherAccess();

        foreach (var delay in RetryDelaysMilliseconds)
        {
            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken);
            }

            try
            {
                return ReadTextOnce(expectedSequenceNumber);
            }
            catch (ExternalException) when (!cancellationToken.IsCancellationRequested)
            {
                // Another application still owns the clipboard; retry shortly.
            }
        }

        return new ClipboardReadResult(
            ClipboardReadStatus.Unavailable,
            SequenceNumber: ClipboardMonitor.CurrentSequenceNumber);
    }

    public async Task<bool> WriteTextAsync(string text, CancellationToken cancellationToken = default)
    {
        EnsureDispatcherAccess();
        ArgumentNullException.ThrowIfNull(text);

        foreach (var delay in RetryDelaysMilliseconds)
        {
            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken);
            }

            try
            {
                var dataObject = new DataObject(DataFormats.UnicodeText, text);
                Clipboard.SetDataObject(dataObject, copy: true);
                return true;
            }
            catch (ExternalException) when (!cancellationToken.IsCancellationRequested)
            {
                // Another application can briefly lock the clipboard.
            }
        }

        return false;
    }

    public static bool ShouldExcludeFromHistory(System.Windows.IDataObject dataObject)
    {
        ArgumentNullException.ThrowIfNull(dataObject);

        if (dataObject.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing", autoConvert: false) ||
            dataObject.GetDataPresent("Clipboard Viewer Ignore", autoConvert: false))
        {
            return true;
        }

        foreach (var format in ZeroMeansExcludedFormats)
        {
            if (!dataObject.GetDataPresent(format, autoConvert: false))
            {
                continue;
            }

            if (!TryReadDword(dataObject.GetData(format, autoConvert: false), out var value) || value == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadDword(object? data, out uint value)
    {
        switch (data)
        {
            case uint unsignedInteger:
                value = unsignedInteger;
                return true;
            case int integer:
                value = unchecked((uint)integer);
                return true;
            case long longInteger when longInteger is >= uint.MinValue and <= uint.MaxValue:
                value = (uint)longInteger;
                return true;
            case byte[] bytes when bytes.Length >= sizeof(uint):
                value = BitConverter.ToUInt32(bytes, 0);
                return true;
            case MemoryStream stream:
                {
                    var bytesFromStream = stream.ToArray();
                    if (bytesFromStream.Length >= sizeof(uint))
                    {
                        value = BitConverter.ToUInt32(bytesFromStream, 0);
                        return true;
                    }

                    break;
                }
        }

        value = 0;
        return false;
    }

    private ClipboardReadResult ReadTextOnce(uint expectedSequenceNumber)
    {
        var sequenceBeforeRead = ClipboardMonitor.CurrentSequenceNumber;
        if (expectedSequenceNumber != 0 && sequenceBeforeRead != expectedSequenceNumber)
        {
            return new ClipboardReadResult(
                ClipboardReadStatus.Stale,
                SequenceNumber: sequenceBeforeRead);
        }

        var dataObject = Clipboard.GetDataObject();
        if (dataObject is null)
        {
            return new ClipboardReadResult(
                ClipboardReadStatus.Empty,
                SequenceNumber: sequenceBeforeRead);
        }

        if (ShouldExcludeFromHistory(dataObject))
        {
            return new ClipboardReadResult(
                ClipboardReadStatus.Excluded,
                SequenceNumber: sequenceBeforeRead);
        }

        if (!dataObject.GetDataPresent(DataFormats.UnicodeText, autoConvert: true))
        {
            return new ClipboardReadResult(
                ClipboardReadStatus.Empty,
                SequenceNumber: sequenceBeforeRead);
        }

        var text = dataObject.GetData(DataFormats.UnicodeText, autoConvert: true) as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ClipboardReadResult(
                ClipboardReadStatus.Empty,
                SequenceNumber: sequenceBeforeRead);
        }

        if (text.Length > MaximumTextBytes || Encoding.UTF8.GetByteCount(text) > MaximumTextBytes)
        {
            return new ClipboardReadResult(
                ClipboardReadStatus.TooLarge,
                SequenceNumber: sequenceBeforeRead);
        }

        var sequenceAfterRead = ClipboardMonitor.CurrentSequenceNumber;
        if (sequenceAfterRead != sequenceBeforeRead)
        {
            return new ClipboardReadResult(
                ClipboardReadStatus.Stale,
                SequenceNumber: sequenceAfterRead);
        }

        return new ClipboardReadResult(
            ClipboardReadStatus.Success,
            text,
            sequenceAfterRead);
    }

    private void EnsureDispatcherAccess()
    {
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException(
                "Le presse-papiers doit être utilisé depuis le thread principal STA.");
        }
    }
}
