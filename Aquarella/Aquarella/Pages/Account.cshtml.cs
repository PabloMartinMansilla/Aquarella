using System.Security.Claims;
using Aquarella.Data;
using Aquarella.Services.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
namespace Aquarella.Pages;
[Authorize]
public sealed class AccountModel(IDbContextFactory<AquarellaDbContext> factory, AccountService accounts, IWebHostEnvironment environment, IConfiguration configuration, ILogger<AccountModel> logger) : PageModel
{
    public User Person { get; private set; } = null!;
    public string? Message { get; private set; }
    public int PendingNotifications { get; private set; }
    public bool Development => environment.IsDevelopment();
    private async Task Load() {
        var id = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!); await using var db = await factory.CreateDbContextAsync();
        Person = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
        PendingNotifications = await db.AccountEmailNotifications.CountAsync(n => n.UserId == id && n.Purpose == "password-changed" && n.SentAt == null && n.CancelledAt == null);
    }
    public async Task OnGetAsync() => await Load();
    public async Task<IActionResult> OnPostLogoutAsync() { await accounts.SignOutAsync(HttpContext); return Redirect("/login"); }
    public async Task<IActionResult> OnPostPasswordAsync() {
        await Load();
        try {
            var accepted = await accounts.SendAsync(Person, "reset", AccountMailOrigin.FromRequest(configuration, environment, Request));
            Message = accepted ? "Solicitud registrada. Se procesará el envío del enlace para cambiar tu contraseña." : "Esperá unos minutos antes de solicitar otro enlace.";
        }
        catch (InvalidOperationException) { Message = "El envío de emails todavía no está configurado."; }
        catch (Exception e) when (e is IOException or DbUpdateException or Microsoft.Data.Sqlite.SqliteException) {
            logger.LogWarning("No se pudo solicitar el cambio de contraseña ({Type}).", e.GetType().Name);
            Message = "No se pudo completar la solicitud. Volvé a intentar.";
        }
        return Page();
    }
    public async Task<IActionResult> OnPostRetryNotificationsAsync() {
        await Load();
        await accounts.RetryPasswordNotificationsAsync(Person.Id);
        await Load();
        Message = PendingNotifications == 0 ? "Las notificaciones pendientes fueron procesadas." : "La notificación sigue pendiente. Los reintentos tienen un intervalo y un máximo de cinco intentos; si persiste, contactá al responsable de Aquarella.";
        return Page();
    }
}
