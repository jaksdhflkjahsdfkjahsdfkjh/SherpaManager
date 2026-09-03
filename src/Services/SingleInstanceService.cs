namespace SherpaManager.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly EventWaitHandle _acknowledgementEvent;
    private readonly EventWaitHandle _shutdownEvent = new(false, EventResetMode.ManualReset);
    private Task? _listener;
    private bool _ownsMutex;
    private bool _disposed;
    private int _handlesDisposed;

    public bool IsPrimaryInstance => _ownsMutex;

    public SingleInstanceService(string instanceKey = "Application")
    {
        var safeKey = string.Concat(instanceKey.Where(character => char.IsLetterOrDigit(character) || character is '.' or '-'));
        if (string.IsNullOrWhiteSpace(safeKey)) safeKey = "Application";
        var prefix = $@"Local\SherpaManager.{safeKey}";
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + ".Activate.v3");
        _acknowledgementEvent = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + ".Acknowledge.v3");
        _mutex = new Mutex(true, prefix + ".SingleInstance.v3", out var createdNew);
        _ownsMutex = createdNew;
    }

    public bool SignalPrimaryInstance(TimeSpan? timeout = null)
    {
        if (_ownsMutex) return true;
        _acknowledgementEvent.Reset();
        _activationEvent.Set();
        try
        {
            // Wait for either a completed window activation or ownership released
            // by a primary that is exiting. Waiting on both closes the small race
            // between a slow save and a zero-time mutex retry. Check the mutex first
            // so ownership wins if acknowledgement and shutdown happen together.
            var result = WaitHandle.WaitAny(
                [_mutex, _acknowledgementEvent], timeout ?? TimeSpan.FromSeconds(4));
            if (result == 0)
            {
                _ownsMutex = true;
                return true;
            }
            if (result == 1) return true;
            return false;
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
            return true;
        }
    }

    public void StartListening(Action activationRequested) => StartListening(() =>
    {
        activationRequested();
        return Task.FromResult(true);
    });

    public void StartListening(Func<Task<bool>> activationRequested)
    {
        ArgumentNullException.ThrowIfNull(activationRequested);
        if (!_ownsMutex || _listener is not null || _disposed) return;
        _listener = Task.Run(async () =>
        {
            var handles = new WaitHandle[] { _shutdownEvent, _activationEvent };
            while (WaitHandle.WaitAny(handles) == 1)
            {
                var activationCompleted = false;
                try
                {
                    activationCompleted = await activationRequested().ConfigureAwait(false);
                }
                catch
                {
                    // A dispatcher operation can be aborted while the primary exits.
                    // Withholding the acknowledgement lets the waiting process take
                    // ownership after the mutex is released instead of losing a launch.
                }

                if (activationCompleted && !_shutdownEvent.WaitOne(0))
                    _acknowledgementEvent.Set();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdownEvent.Set();
        if (_ownsMutex)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
            _ownsMutex = false;
        }

        // The listener can be awaiting a dispatcher operation owned by the thread
        // that is disposing us. Waiting here would deadlock application shutdown.
        // Release single-instance ownership immediately and dispose wait handles
        // once the listener has observed the shutdown signal.
        if (_listener is { IsCompleted: false } listener)
            _ = listener.ContinueWith(_ => DisposeHandles(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        else
            DisposeHandles();
    }

    private void DisposeHandles()
    {
        if (Interlocked.Exchange(ref _handlesDisposed, 1) != 0) return;
        _shutdownEvent.Dispose();
        _acknowledgementEvent.Dispose();
        _activationEvent.Dispose();
        _mutex.Dispose();
    }
}
