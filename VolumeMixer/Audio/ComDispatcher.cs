using System.Collections.Concurrent;

namespace VolumeMixer.Audio;

/// <summary>Dedicated MTA thread that owns all COM CoreAudio objects.
/// Preserves synchronous public IAudioController API by marshaling calls and blocking.</summary>
internal sealed class ComDispatcher : IDisposable
{
    private readonly Thread _thread;
    private readonly BlockingCollection<Action> _queue = new();
    private bool _disposed;

    /// <summary>The MTA thread managed by this dispatcher.</summary>
    internal Thread MtaThread => _thread;

    public ComDispatcher()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "CoreAudio-MTA" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    private void Run()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
            action();
    }

    /// <summary>Marshal a function to the MTA thread and return the result synchronously.</summary>
    public T Invoke<T>(Func<T> func)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ComDispatcher));
        T? result = default;
        Exception? error = null;
        var done = new ManualResetEventSlim(false);
        _queue.Add(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        });
        done.Wait();
        done.Dispose();
        if (error is not null) throw error;
        return result!;
    }

    /// <summary>Marshal an action to the MTA thread and block until complete.</summary>
    public void Invoke(Action action)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ComDispatcher));
        Exception? error = null;
        var done = new ManualResetEventSlim(false);
        _queue.Add(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        });
        done.Wait();
        done.Dispose();
        if (error is not null) throw error;
    }

    /// <summary>Fire-and-forget on MTA thread.</summary>
    public void Post(Action action)
    {
        if (_disposed) return;
        _queue.Add(action);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.CompleteAdding();
        _thread.Join();
        _queue.Dispose();
    }
}
