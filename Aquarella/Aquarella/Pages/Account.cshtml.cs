using System.Security.Claims;
using Aquarella.Data;
using Aquarella.Services.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
namespace Aquarella.Pages;
[Authorize]
public sealed class AccountModel(IDbContextFactory<AquarellaDbContext> factory, AccountService accounts, IWebHostEnvironment environment, IConfiguration configuration) : PageModel
{
    public User Person { get; private set; } = null!;
    public string? Message { get; private set; }
    public bool Development => environment.IsDevelopment();
    private async Task Load() { var id = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!); await using var db = await factory.CreateDbContextAsync(); Person = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id); }
    public async Task OnGetAsync() => await Load();
    public async Task<IActionResult> OnPostLogoutAsync() { await accounts.SignOutAsync(HttpContext); return Redirect("/login"); }
    public async Task<IActionResult> OnPostPasswordAsync() {
        await Load();
        try {
            var origin = configuration["Accounts:PublicOrigin"];
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https") {
                if (!Development || Request.Host.Host != "localhost") throw new InvalidOperationException("Configurá la URL pública.");
                origin = $"{Request.Scheme}://{Request.Host}";
            } else origin = uri.GetLeftPart(UriPartial.Authority);
            await accounts.SendAsync(Person, "reset", origin!); Message = "Solicitamos un enlace para cambiar tu contraseña.";
        }
        catch (InvalidOperationException) { Message = "El envío de emails todavía no está configurado."; }
        return Page();
    }
}
