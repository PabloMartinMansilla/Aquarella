using Microsoft.JSInterop;
namespace Aquarella.Services;

// Development-only session marker. This does not authenticate or authorize requests.
public sealed class LocalSessionStore(IJSRuntime js, IWebHostEnvironment environment) : IAsyncDisposable
{
    private IJSObjectReference? storage;
    private Task? initialization;
    public bool Available => environment.IsDevelopment();
    public bool IsSignedIn { get; private set; }
    public string? UserId => IsSignedIn ? "pablo" : null;
    private bool tutorialRequested;
    public void RequestTutorial() => tutorialRequested = true;
    public bool ConsumeTutorialRequest() { var requested = tutorialRequested; tutorialRequested = false; return requested; }
    public string? Notice { get; private set; }
    public event Action? Changed;
    public Task InitializeAsync() => initialization ??= Initialize();
    private async Task Initialize()
    {
        if (!Available) return;
        storage = await js.InvokeAsync<IJSObjectReference>("import", "./local-session.js");
        IsSignedIn = await storage.InvokeAsync<bool>("load");
        Changed?.Invoke();
    }
    public async Task<bool> SignInAsync(string username, string password)
    {
        if (!Available || !string.Equals(username, "pablo", StringComparison.Ordinal) || !string.Equals(password, "pablo", StringComparison.Ordinal)) return false;
        await InitializeAsync();
        await storage!.InvokeVoidAsync("save");
        IsSignedIn = true;
        Notice = "Sesión iniciada. Bienvenido, Pablo.";
        Changed?.Invoke();
        return true;
    }
    public async Task SignOutAsync()
    {
        await InitializeAsync();
        if (storage is not null) await storage.InvokeVoidAsync("clear");
        IsSignedIn = false;
        Notice = "Sesión cerrada.";
        Changed?.Invoke();
    }
    public async ValueTask DisposeAsync()
    {
        if (storage is not null) { try { await storage.DisposeAsync(); } catch (JSDisconnectedException) { } }
    }
}


