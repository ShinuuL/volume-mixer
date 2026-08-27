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
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                // Nunca deixe uma exceção matar o loop da thread MTA. Se um
                // callback COM (ex: RefreshSessionCache) lançar, registramos e
                // continuamos — caso contrário o processo congela/morre em
                // silêncio e novos apps de áudio deixam de ser detectados.
                try { VolumeMixer.Infrastructure.AppLog.Instance.Error("exceção não capturada na thread MTA", ex); }
                catch { /* logging must never crash the loop */ }
            }
        }
    }

    /// <summary>Marshal a function to the MTA thread and return the result synchronously.
    /// If already on the MTA thread, executes inline to avoid self-deadlock.</summary>
    public T Invoke<T>(Func<T> func)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ComDispatcher));

        // Fast path: reentrant call from the MTA thread itself — execute inline.
        if (Thread.CurrentThread == _thread)
            return func();

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

    /// <summary>Marshal an action to the MTA thread and block until complete.
    /// If already on the MTA thread, executes inline to avoid self-deadlock.</summary>
    public void Invoke(Action action)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ComDispatcher));

        // Fast path: reentrant call from the MTA thread itself — execute inline.
        if (Thread.CurrentThread == _thread)
        {
            action();
            return;
        }

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

    /// <summary>Fire-and-forget on MTA thread. Silently ignored after Dispose.</summary>
    public void Post(Action action)
    {
        if (_disposed) return;
        try { _queue.Add(action); }
        catch (InvalidOperationException) { }
        // ObjectDisposedException inherits from InvalidOperationException,
        // so the catch above already covers the queue-disposed race path.
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
