using Microsoft.Win32;

namespace PressHistory.Services;

public sealed class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PressHistory";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return string.Equals(value, BuildStartupCommand(), StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                        ?? throw new InvalidOperationException(
                            "Impossible d’ouvrir les paramètres de démarrage de Windows.");

        if (enabled)
        {
            key.SetValue(ValueName, BuildStartupCommand(), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public string BuildStartupCommand()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Le chemin de l’application est introuvable.");
        }

        return $"\"{Path.GetFullPath(executablePath)}\" --background";
    }
}
