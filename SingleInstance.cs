using System.Runtime.InteropServices;

namespace MessengerDesktop;

internal static class NativeForeground
{
    public static void AllowAnyProcess() => AllowSetForegroundWindow(unchecked((uint)-1));

    public static void BringToFront(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        SetForegroundWindow(handle);
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}

// The first process owns the mutex and waits on the event. A later process
// lets this one take the foreground, signals the event, and exits.
internal sealed class SingleInstance : IDisposable
{
    internal const string MutexName = @"Local\MessengerDesktop.SingleInstance";
    internal const string ActivateEventName = @"Local\MessengerDesktop.Activate";

    private readonly Mutex mutex;
    private readonly EventWaitHandle activate;
    private readonly bool isPrimary;
    private RegisteredWaitHandle? wait;
    private bool listening;
    private bool disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle activate, bool isPrimary)
    {
        this.mutex = mutex;
        this.activate = activate;
        this.isPrimary = isPrimary;
    }

    public bool IsPrimary => isPrimary;

    public static SingleInstance Acquire()
    {
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        var mutex = new Mutex(false, MutexName);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        return new SingleInstance(mutex, activate, acquired);
    }

    public void SignalExisting()
    {
        if (isPrimary)
            throw new InvalidOperationException("The first instance does not signal itself.");
        NativeForeground.AllowAnyProcess();
        activate.Set();
    }

    public void Listen(Action onActivate)
    {
        if (!isPrimary)
            throw new InvalidOperationException("Only the first instance waits for activation.");
        ArgumentNullException.ThrowIfNull(onActivate);
        if (listening) return;
        listening = true;
        wait = ThreadPool.RegisterWaitForSingleObject(
            activate,
            (_, timedOut) =>
            {
                if (timedOut) return;
                try
                {
                    onActivate();
                }
                catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException)
                {
                    // The window can close between the signal and the callback.
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        wait?.Unregister(activate);
        wait = null;
        if (isPrimary)
        {
            try { mutex.ReleaseMutex(); }
            catch (ApplicationException)
            {
                // Already released.
            }
        }

        activate.Dispose();
        mutex.Dispose();
    }
}
