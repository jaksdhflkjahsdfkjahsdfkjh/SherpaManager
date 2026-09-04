using System.IO.MemoryMappedFiles;
using System.Text;

namespace SherpaManager.Services;

public sealed class SingleInstanceService : IDisposable
{
    // Large enough for any profile name a person will type, small enough that a
    // hostile or corrupt value cannot make the primary allocate anything notable.
    private const int RequestCapacity = 2048;
    private const int MaximumPayloadBytes = RequestCapacity - sizeof(int);

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly EventWaitHandle _acknowledgementEvent;
    private readonly EventWaitHandle _shutdownEvent = new(false, EventResetMode.ManualReset);
    private readonly Mutex _requestLock;
    private readonly string _requestName;
    private readonly MemoryMappedFile? _requestBuffer;
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
        _requestName = prefix + ".Request.v1";
        _requestLock = new Mutex(false, prefix + ".RequestLock.v1");
        _mutex = new Mutex(true, prefix + ".SingleInstance.v3", out var createdNew);
        _ownsMutex = createdNew;

        // Only the primary owns the shared buffer, so its lifetime matches the
        // process that reads from it. A secondary opens it just long enough to
        // write, which removes any question of who cleans it up.
        if (_ownsMutex)
        {
            try { _requestBuffer = MemoryMappedFile.CreateOrOpen(_requestName, RequestCapacity); }
            catch (IOException) { /* Requests degrade to a plain window activation. */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    public bool SignalPrimaryInstance(TimeSpan? timeout = null) => SignalPrimaryInstance(null, timeout);

    /// <summary>
    /// Hands <paramref name="payload"/> to the running instance and waits for it
    /// to finish acting on it. A payload that cannot be delivered still signals,
    /// so the user gets a window rather than nothing.
    /// </summary>
    public bool SignalPrimaryInstance(string? payload, TimeSpan? timeout = null)
    {
        if (_ownsMutex) return true;
        if (!string.IsNullOrWhiteSpace(payload)) TryWriteRequest(payload);
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
        StartListening(_ => activationRequested());
    }

    /// <summary>
    /// The handler receives the payload sent by the secondary instance, or null
    /// when it only asked for the window.
    /// </summary>
    public void StartListening(Func<string?, Task<bool>> activationRequested)
    {
        ArgumentNullException.ThrowIfNull(activationRequested);
        if (!_ownsMutex || _listener is not null || _disposed) return;
        _listener = Task.Run(async () =>
        {
            var handles = new WaitHandle[] { _shutdownEvent, _activationEvent };
            while (WaitHandle.WaitAny(handles) == 1)
            {
                var activationCompleted = false;
                // Consume the request before running the handler, so a handler that
                // throws cannot leave a stale payload for the next activation.
                var payload = TryTakeRequest();
                try
                {
                    activationCompleted = await activationRequested(payload).ConfigureAwait(false);
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

    private void TryWriteRequest(string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        if (bytes.Length > MaximumPayloadBytes) return;

        var held = false;
        try
        {
            try { held = _requestLock.WaitOne(TimeSpan.FromSeconds(2)); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) return;

            using var buffer = MemoryMappedFile.OpenExisting(_requestName, MemoryMappedFileRights.ReadWrite);
            using var accessor = buffer.CreateViewAccessor(0, RequestCapacity);
            accessor.WriteArray(sizeof(int), bytes, 0, bytes.Length);
            accessor.Write(0, bytes.Length);
        }
        catch (FileNotFoundException) { /* An older primary has no buffer. */ }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally
        {
            if (held)
            {
                try { _requestLock.ReleaseMutex(); }
                catch (ApplicationException) { }
            }
        }
    }

    private string? TryTakeRequest()
    {
        if (_requestBuffer is null) return null;

        var held = false;
        try
        {
            try { held = _requestLock.WaitOne(TimeSpan.FromSeconds(2)); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) return null;

            using var accessor = _requestBuffer.CreateViewAccessor(0, RequestCapacity);
            var length = accessor.ReadInt32(0);
            accessor.Write(0, 0);
            if (length <= 0 || length > MaximumPayloadBytes) return null;

            var bytes = new byte[length];
            accessor.ReadArray(sizeof(int), bytes, 0, length);
            var payload = Encoding.UTF8.GetString(bytes);
            return string.IsNullOrWhiteSpace(payload) ? null : payload;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        finally
        {
            if (held)
            {
                try { _requestLock.ReleaseMutex(); }
                catch (ApplicationException) { }
            }
        }
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
        _requestBuffer?.Dispose();
        _requestLock.Dispose();
        _mutex.Dispose();
    }
}
