using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace PressHistory.Services;

public sealed class SingleInstanceService : IDisposable
{
    private static readonly string InstanceScope = CreateInstanceScope();
    private static readonly string MutexName = $@"Local\PressHistory.SingleInstance.v1.{InstanceScope}";
    private static readonly string PipeName = $"PressHistory.SingleInstance.v1.{InstanceScope}";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _listenerCancellation = new();
    private Task? _listenerTask;
    private bool _disposed;

    public SingleInstanceService()
    {
        _mutex = new Mutex(initiallyOwned: false, MutexName);

        try
        {
            IsPrimaryInstance = _mutex.WaitOne(0, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            IsPrimaryInstance = true;
        }
    }

    public bool IsPrimaryInstance { get; }

    public void StartListening(Action showPrimaryInstance)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(showPrimaryInstance);

        if (!IsPrimaryInstance || _listenerTask is not null)
        {
            return;
        }

        _listenerTask = Task.Run(() => ListenLoopAsync(showPrimaryInstance, _listenerCancellation.Token));
    }

    public static async Task SignalPrimaryInstanceAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                await using var client = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(600, cancellationToken).ConfigureAwait(false);
                await client.WriteAsync(
                    "SHOW\n"u8.ToArray(),
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (exception is
                                               TimeoutException or
                                               IOException or
                                               UnauthorizedAccessException)
            {
                if (attempt == 3)
                {
                    return;
                }

                await Task.Delay(150 * (attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listenerCancellation.Cancel();
        _listenerCancellation.Dispose();

        if (IsPrimaryInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The process is already relinquishing the abandoned mutex.
            }
        }

        _mutex.Dispose();
    }

    private static async Task ListenLoopAsync(Action showPrimaryInstance, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                var messageBuffer = new byte[5];
                var bytesRead = 0;
                while (bytesRead < messageBuffer.Length)
                {
                    var read = await server.ReadAsync(
                        messageBuffer.AsMemory(bytesRead),
                        cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    bytesRead += read;
                }

                if (bytesRead == messageBuffer.Length &&
                    messageBuffer.AsSpan().SequenceEqual("SHOW\n"u8))
                {
                    showPrimaryInstance();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                // Recreate the server after a transient client disconnect.
            }
        }
    }

    private static string CreateInstanceScope()
    {
        var identity = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var safeIdentity = new string(identity
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .ToArray());
        return $"{safeIdentity}.{Process.GetCurrentProcess().SessionId}";
    }
}
