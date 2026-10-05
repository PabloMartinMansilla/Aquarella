using Aquarella.Services.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Aquarella.Pages;
[AllowAnonymous]
public sealed class AccessModel(AccountService accounts, IWebHostEnvironment environment, ILogger<AccessModel> logger, IConfiguration configuration, DevelopmentAccess developmentAccess, IAccountEmail emailProvider) : PageModel
{
    [BindProperty] public string FirstName { get; set; } = "";
    [BindProperty] public string LastName { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string Confirmation { get; set; } = "";
    [BindProperty] public bool Remember { get; set; }
    [BindProperty(SupportsGet = true)] public string? Token { get; set; }
    [BindProperty(SupportsGet = true)] public string? Legacy { get; set; }
    public string Flow => Request.Path.Value!.Trim('/');
    public bool Success { get; private set; }
    public string? Message { get; private set; }
    public bool Development => environment.IsDevelopment();
    public bool EmailAvailable => emailProvider.Available;
    public string Title => Flow switch { "crear-cuenta" => "Crear cuenta", "verificar-email" => "Verificá tu correo", "verificar-correo" => "Verificar correo", "recuperar-contrasena" => "Recuperar contraseña", "nueva-contrasena" => "Crear nueva contraseña", _ => "Iniciar sesión" };
    private string Origin {
        get {
            if (Uri.TryCreate(configuration["Accounts:PublicOrigin"], UriKind.Absolute, out var origin) && origin.Scheme == "https") return origin.GetLeftPart(UriPartial.Authority);
            if (Development && Request.Host.Host == "localhost") return $"{Request.Scheme}://{Request.Host}";
            throw new InvalidOperationException("Configurá Accounts:PublicOrigin con la URL HTTPS pública.");
        }
    }
    public async Task<IActionResult> OnGetAsync() {
        if (!EmailAvailable && Flow is "crear-cuenta" or "verificar-email" or "recuperar-contrasena")
            Message = "El servicio de correo todavía no está disponible. El registro y la recuperación de contraseña estarán disponibles cuando se configure.";
        if (Flow == "verificar-email") Email = TempData.Peek("RegistrationEmail") as string ?? "";
        if (Flow == "login" && User.Identity?.IsAuthenticated == true) return Redirect("/");
        if (Flow == "crear-cuenta" && Legacy is not null && (!Development || !await accounts.TokenValidAsync(Legacy, "legacy"))) { Message = "El enlace de vinculación no es válido o venció."; }
        if (Flow == "verificar-correo") { Success = await accounts.ConsumeAsync(Token ?? "", "verify"); Message = Success ? "Correo verificado correctamente" : "El enlace no es válido, ya fue utilizado o venció."; }
        if (Flow == "nueva-contrasena" && !await accounts.TokenValidAsync(Token ?? "", "reset")) Message = "El enlace no es válido, ya fue utilizado o venció.";
        return Page();
    }
    public async Task<IActionResult> OnPostAsync() {
        if (!EmailAvailable && Flow is "crear-cuenta" or "verificar-email" or "recuperar-contrasena") {
            Message = "El servicio de correo todavía no está disponible. No se realizó ningún cambio ni se enviaron enlaces.";
            return Page();
        }
        // Required fields vary by flow; optional form fields are intentionally excluded.
        ModelState.Clear();
        if (Flow is "login" or "crear-cuenta" or "recuperar-contrasena" or "verificar-email") {
            if (!AccountService.ValidEmail(Email)) ModelState.AddModelError(nameof(Email), "Ingresá un correo electrónico válido.");
        }
        if (Flow == "crear-cuenta") {
            if (string.IsNullOrWhiteSpace(FirstName) || FirstName.Trim().Length > 100) ModelState.AddModelError(nameof(FirstName), "Ingresá tu nombre (hasta 100 caracteres).");
            if (string.IsNullOrWhiteSpace(LastName) || LastName.Trim().Length > 100) ModelState.AddModelError(nameof(LastName), "Ingresá tu apellido (hasta 100 caracteres).");
        }
        if (Flow is "crear-cuenta" or "nueva-contrasena") {
            if (!AccountService.ValidPassword(Password)) ModelState.AddModelError(nameof(Password), "Usá entre 12 y 256 caracteres. Podés usar una frase larga.");
            if (!string.Equals(Password, Confirmation, StringComparison.Ordinal)) ModelState.AddModelError(nameof(Confirmation), "Las contraseñas no coinciden.");
        }
        if (Flow == "login" && (Password.Length == 0 || Password.Length > 256)) ModelState.AddModelError(nameof(Password), "Ingresá tu contraseña.");
        if (!ModelState.IsValid) return Page();
        try {
            switch (Flow) {
                case "login":
                    var user = await accounts.AuthenticateAsync(Email, Password);
                    if (user is null) { Message = "Correo electrónico o contraseña incorrectos."; return Page(); }
                    await accounts.SignInAsync(HttpContext, user, Remember); return Redirect("/");
                case "crear-cuenta":
                    if (Legacy is not null && !Development) { Message = "Enlace no válido."; return Page(); }
                    var result = await accounts.RegisterAsync(FirstName, LastName, Email, Password, Legacy);
                    if (result.User is null) { ModelState.AddModelError(nameof(Email), result.Error!); return Page(); }
                    await TrySend(result.User, "verify");
                    TempData["RegistrationEmail"] = result.User.Email;
                    return Redirect("/verificar-email");
                case "recuperar-contrasena":
                case "verificar-email":
                    var existing = await accounts.FindAsync(Email);
                    if (existing?.PasswordHash is not null && (Flow != "verificar-email" || !existing.EmailVerified)) await TrySend(existing, Flow == "verificar-email" ? "verify" : "reset");
                    Success = true; Message = Flow == "verificar-email" ? "Si corresponde, recibirás un nuevo enlace de verificación." : "Si existe una cuenta asociada a ese correo, recibirás un enlace para restablecer tu contraseña.";
                    return Page();
                case "nueva-contrasena":
                    Success = await accounts.ConsumeAsync(Token ?? "", "reset", Password);
                    Message = Success ? "Contraseña actualizada correctamente." : "El enlace no es válido, ya fue utilizado o venció."; return Page();
                default: return BadRequest();
            }
        } catch (Exception e) when (e is InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException or Microsoft.Data.Sqlite.SqliteException or IOException) {
            logger.LogError("La operación de cuenta {Flow} no pudo completarse ({Type}).", Flow, e.GetType().Name);
            Message = "No se pudo completar la operación. Volvé a intentar."; return Page();
        }
    }
    public async Task<IActionResult> OnPostDevelopmentAsync() {
        if (!Development || Flow != "login") return NotFound();
        try {
            var user = await developmentAccess.FindUserAsync();
            if (user is null) { Message = "No se encontró el usuario de desarrollo asociado a un negocio. Revisá la configuración local."; return Page(); }
            await accounts.SignInAsync(HttpContext, user, remember: false);
            return Redirect("/");
        } catch (Exception e) when (e is InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException or Microsoft.Data.Sqlite.SqliteException) {
            logger.LogError("No se pudo iniciar el acceso de desarrollo ({Type}).", e.GetType().Name);
            Message = "No se pudo iniciar el acceso de prueba. Volvé a intentar."; return Page();
        }
    }
    private async Task TrySend(Aquarella.Data.User user, string purpose) {
        try { await accounts.SendAsync(user, purpose, Origin); }
        catch (Exception e) when (e is InvalidOperationException or IOException) { logger.LogWarning("El envío de {Purpose} no está disponible ({Type}).", purpose, e.GetType().Name); }
    }
}
