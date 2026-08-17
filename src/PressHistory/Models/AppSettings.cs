namespace PressHistory.Models;

public sealed class AppSettings
{
    public bool CaptureEnabled { get; set; } = true;

    public bool HasShownTrayHint { get; set; }

    public int MaxEntries { get; set; } = 250;

    public AppSettings Snapshot()
    {
        return new AppSettings
        {
            CaptureEnabled = CaptureEnabled,
            HasShownTrayHint = HasShownTrayHint,
            MaxEntries = MaxEntries
        };
    }
}
