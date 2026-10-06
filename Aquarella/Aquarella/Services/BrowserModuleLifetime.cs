using Microsoft.JSInterop;
namespace Aquarella.Services;

// Each reference is released even if browser cleanup is impossible after disconnection.
public static class BrowserModuleLifetime
{
    public static async ValueTask ReleaseAsync(IJSObjectReference module, string? stop = null, params object?[] args)
    {
        try
        {
            if (stop is not null) await module.InvokeVoidAsync(stop, args);
        }
        catch (Exception e) when (e is JSException or JSDisconnectedException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            try { await module.DisposeAsync(); }
            catch (Exception e) when (e is JSException or JSDisconnectedException or OperationCanceledException or ObjectDisposedException) { }
        }
    }
}
