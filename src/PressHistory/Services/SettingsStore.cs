using System.Text;
using System.Text.Json;
using PressHistory.Models;

namespace PressHistory.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SettingsStore(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(
                _paths.SettingsFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4 * 1024,
                useAsync: true);

            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (settings is null)
            {
                return new AppSettings();
            }

            settings.MaxEntries = Math.Clamp(settings.MaxEntries, 20, 1_000);
            return settings;
        }
        catch (Exception exception) when (exception is
                                           FileNotFoundException or
                                           DirectoryNotFoundException or
                                           JsonException or
                                           IOException or
                                           UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporaryFile = _paths.SettingsFile + ".tmp";

        try
        {
            Directory.CreateDirectory(_paths.DataDirectory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(
                temporaryFile,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryFile, _paths.SettingsFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                try
                {
                    File.Delete(temporaryFile);
                }
                catch (IOException)
                {
                    // A later save can reuse the same temporary path.
                }
            }

            _writeLock.Release();
        }
    }
}
