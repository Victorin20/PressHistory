using System.Text;
using System.Text.Json;
using PressHistory.Models;

namespace PressHistory.Services;

public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public HistoryStore(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<IReadOnlyList<ClipboardEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_paths.DataDirectory);

        try
        {
            return await ReadFileAsync(_paths.HistoryFile, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsRecoverableReadError(exception))
        {
            PreserveCorruptedFile();

            try
            {
                return await ReadFileAsync(_paths.HistoryBackupFile, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception backupException) when (IsRecoverableReadError(backupException))
            {
                return Array.Empty<ClipboardEntry>();
            }
        }
    }

    public async Task SaveAsync(
        IReadOnlyCollection<ClipboardEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        var temporaryFile = _paths.HistoryFile + ".tmp";

        try
        {
            Directory.CreateDirectory(_paths.DataDirectory);

            if (entries.Count == 0)
            {
                DeleteHistoryFiles();
                return;
            }

            var json = await Task.Run(
                    () => JsonSerializer.Serialize(entries, JsonOptions),
                    cancellationToken)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(
                temporaryFile,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);

            if (File.Exists(_paths.HistoryFile))
            {
                File.Replace(
                    temporaryFile,
                    _paths.HistoryFile,
                    _paths.HistoryBackupFile,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryFile, _paths.HistoryFile);
            }

            // Keep the recovery copy aligned with the current history so an item
            // explicitly deleted by the user is not retained in an older backup.
            File.Copy(_paths.HistoryFile, _paths.HistoryBackupFile, overwrite: true);
            DeleteCorruptedCopies();
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
                    // A future save will safely overwrite this temporary file.
                }
            }

            _writeLock.Release();
        }
    }

    private static bool IsRecoverableReadError(Exception exception)
    {
        return exception is FileNotFoundException
            or DirectoryNotFoundException
            or JsonException
            or IOException
            or UnauthorizedAccessException;
    }

    private static async Task<IReadOnlyList<ClipboardEntry>> ReadFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            useAsync: true);

        return await JsonSerializer.DeserializeAsync<List<ClipboardEntry>>(
                   stream,
                   JsonOptions,
                   cancellationToken).ConfigureAwait(false)
               ?? new List<ClipboardEntry>();
    }

    private void PreserveCorruptedFile()
    {
        if (!File.Exists(_paths.HistoryFile))
        {
            return;
        }

        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var preservedPath = Path.Combine(_paths.DataDirectory, $"history.corrupt-{timestamp}.json");
            File.Copy(_paths.HistoryFile, preservedPath, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Recovery through the backup remains possible even if preservation fails.
        }
    }

    private void DeleteHistoryFiles()
    {
        foreach (var path in new[]
                 {
                     _paths.HistoryFile,
                     _paths.HistoryBackupFile,
                     _paths.HistoryFile + ".tmp"
                 })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        DeleteCorruptedCopies();
    }

    private void DeleteCorruptedCopies()
    {
        if (!Directory.Exists(_paths.DataDirectory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(
                     _paths.DataDirectory,
                     "history.corrupt-*.json",
                     SearchOption.TopDirectoryOnly))
        {
            File.Delete(path);
        }
    }
}
