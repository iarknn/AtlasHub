using System;

namespace AtlasHub.Services;

public sealed class AppEventBus
{
    public event EventHandler? ProvidersChanged;

    public void RaiseProvidersChanged()
        => ProvidersChanged?.Invoke(this, EventArgs.Empty);

    // Toast bildirimi (string payload)
    public event EventHandler<string>? Toast;

    public void RaiseToast(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        Toast?.Invoke(this, message);
    }
}