namespace PressHistory.Services;

public sealed class AppPaths
{
    public AppPaths(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PressHistory");
    }

    public string DataDirectory { get; }

    public string HistoryFile => Path.Combine(DataDirectory, "history.json");

    public string HistoryBackupFile => Path.Combine(DataDirectory, "history.bak.json");

    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");
}
