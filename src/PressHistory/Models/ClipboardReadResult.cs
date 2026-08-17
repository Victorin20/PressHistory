namespace PressHistory.Models;

public enum ClipboardReadStatus
{
    Success,
    Empty,
    Excluded,
    TooLarge,
    Stale,
    Unavailable
}

public sealed record ClipboardReadResult(
    ClipboardReadStatus Status,
    string? Text = null,
    uint SequenceNumber = 0);
