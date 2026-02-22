namespace AtlasHub.Services;

public sealed class AppEventBus
{
    public event EventHandler? ProvidersChanged;

    public void RaiseProvidersChanged()
        => ProvidersChanged?.Invoke(this, EventArgs.Empty);

    // NEW: toast notifications
    public event EventHandler<string>? Toast;

    public void RaiseToast(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Toast?.Invoke(this, message);
    }
}
