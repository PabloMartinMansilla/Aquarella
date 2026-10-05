using System.Security.Claims;
using Aquarella.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
namespace Aquarella.Services.Accounts;

public sealed class AccountSession(AuthenticationStateProvider authentication, AccountService accounts, IDbContextFactory<AquarellaDbContext> factory, IJSRuntime? js = null)
{
    private ClaimsPrincipal principal = new();
    public bool IsSignedIn => principal.Identity?.IsAuthenticated == true;
    public string? UserId => principal.FindFirstValue(ClaimTypes.NameIdentifier);
    public string DisplayName => principal.Identity?.Name ?? "";
    public string? Notice => null;
    public event Action? Changed;
    private bool replay;
    public void RequestTutorial() => replay = true;
    public bool ConsumeTutorialRequest() { var value = replay; replay = false; return value; }
    public async Task InitializeAsync() { principal = (await authentication.GetAuthenticationStateAsync()).User; }
    public async Task<Guid> RequireUserAsync() {
        await InitializeAsync(); if (!await accounts.IsSessionValidAsync(principal) || !Guid.TryParse(UserId, out var id)) throw new InvalidOperationException("Tu sesión terminó. Iniciá sesión nuevamente."); return id;
    }
    public async Task<bool> CompletedTutorialAsync() {
        var id = await RequireUserAsync(); await using var db = await factory.CreateDbContextAsync(); var user = await db.Users.SingleAsync(u => u.Id == id);
        // One-time migration of the old developer's tutorial preference; never applied to other accounts.
        if (!user.HasCompletedOnboarding && user.Username == "pablo" && js is not null) {
            try {
                await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./onboarding.js");
                if (await module.InvokeAsync<bool>("readLegacyCompletion")) { user.HasCompletedOnboarding = true; user.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(); }
            } catch (JSException) { /* The new per-user database state remains authoritative. */ }
        }
        return user.HasCompletedOnboarding;
    }
    public async Task CompleteTutorialAsync() { var id = await RequireUserAsync(); await using var db = await factory.CreateDbContextAsync(); await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.HasCompletedOnboarding, true).SetProperty(u => u.UpdatedAt, DateTime.UtcNow)); Changed?.Invoke(); }
}
public sealed class AccountAuthenticationStateProvider(ILoggerFactory logger, IServiceScopeFactory scopes) : RevalidatingServerAuthenticationStateProvider(logger)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(10);
    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken cancellationToken) { using var scope = scopes.CreateScope(); return await scope.ServiceProvider.GetRequiredService<AccountService>().IsSessionValidAsync(state.User); }
}
