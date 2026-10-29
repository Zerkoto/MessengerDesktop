namespace MessengerDesktop;

// Toast clicks can arrive before the window exists. Keep the latest one.
internal sealed class ToastBridge
{
    private readonly object gate = new();
    private Action<string?>? handler;
    private string? pending;
    private bool hasPending;

    public void Publish(string? argument)
    {
        Action<string?>? target;
        lock (gate)
        {
            if (handler is null)
            {
                pending = argument;
                hasPending = true;
                return;
            }

            target = handler;
        }

        target(argument);
    }

    public void Attach(Action<string?> onActivated)
    {
        ArgumentNullException.ThrowIfNull(onActivated);
        string? queued = null;
        bool deliver = false;
        lock (gate)
        {
            handler = onActivated;
            if (!hasPending) return;
            queued = pending;
            pending = null;
            hasPending = false;
            deliver = true;
        }

        if (deliver) onActivated(queued);
    }
}
