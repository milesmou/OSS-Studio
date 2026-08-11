using System.IO.Pipes;

namespace OSSStudio.Services;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = "Local\\OSSStudio.SingleInstance.3E62C144";
    private const string PipeName = "OSSStudio.Activation.3E62C144";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _activationLock = new();
    private Action? _activationHandler;
    private bool _activationPending;
    private bool _disposed;
    private Task? _listener;

    public SingleInstanceCoordinator()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsPrimary = createdNew;
        if (IsPrimary)
        {
            _listener = ListenAsync(_shutdown.Token);
        }
    }

    public bool IsPrimary { get; }

    public void SetActivationHandler(Action handler)
    {
        var invokePending = false;
        lock (_activationLock)
        {
            _activationHandler = handler;
            if (_activationPending)
            {
                _activationPending = false;
                invokePending = true;
            }
        }

        if (invokePending)
        {
            handler();
        }
    }

    public bool NotifyPrimary()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);
                client.Connect(100);
                client.WriteByte(1);
                client.Flush();
                return true;
            }
            catch (TimeoutException)
            {
                Thread.Sleep(50);
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
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
                await server.WaitForConnectionAsync(cancellationToken);
                var buffer = new byte[1];
                if (await server.ReadAsync(buffer, cancellationToken) > 0)
                {
                    RequestActivation();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    private void RequestActivation()
    {
        Action? handler;
        lock (_activationLock)
        {
            handler = _activationHandler;
            if (handler is null)
            {
                _activationPending = true;
                return;
            }
        }

        handler();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        if (IsPrimary)
        {
            _mutex.ReleaseMutex();
        }
        _mutex.Dispose();
        _shutdown.Dispose();
    }
}
