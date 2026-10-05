using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using Aquarella.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aquarella.Services.Accounts;

public sealed class AccountService(IDbContextFactory<AquarellaDbContext> factory, IDataProtectionProvider protection, IAccountEmail email)
{
    private readonly PasswordHasher<User> hasher = new();
    private readonly ITimeLimitedDataProtector tokens = protection.CreateProtector("Aquarella.AccountTokens.v1").ToTimeLimitedDataProtector();
    private static readonly User dummy = new();
    private static readonly string dummyHash = new PasswordHasher<User>().HashPassword(dummy, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    public static string Normalize(string email) => email.Trim().ToUpperInvariant();
    public static bool ValidEmail(string value) => value.Trim().Length <= 254 && new EmailAddressAttribute().IsValid(value.Trim());
    public static bool ValidPassword(string value) => value.Length is >= 12 and <= 256;
    public async Task<User?> FindAsync(string address) {
        await using var db = await factory.CreateDbContextAsync(); var normalized = Normalize(address);
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedEmail == normalized);
    }
    public async Task<(User? User, string? Error)> RegisterAsync(string first, string last, string address, string password, string? legacyToken = null) {
        if (string.IsNullOrWhiteSpace(first) || first.Trim().Length > 100 || string.IsNullOrWhiteSpace(last) || last.Trim().Length > 100 || !ValidEmail(address) || !ValidPassword(password)) return (null, "Datos de cuenta inválidos.");
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync();
        var normalized = Normalize(address);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized)) return (null, "Este correo ya pertenece a una cuenta.");
        User user;
        if (legacyToken is not null) {
            var ticket = await ReadToken(db, legacyToken, "legacy");
            if (ticket is null) return (null, "El enlace de vinculación no es válido o venció.");
            user = await db.Users.SingleAsync(u => u.Id == ticket.UserId);
            if (user.Email is not null || user.PasswordHash is not null) return (null, "Esta cuenta ya fue vinculada.");
            ticket.UsedAt = DateTime.UtcNow;
        } else {
            user = new User { Username = Guid.NewGuid().ToString("N") };
            db.Users.Add(user); db.Businesses.Add(new Business { User = user, LegacyImported = true });
        }
        user.FirstName = first.Trim(); user.LastName = last.Trim(); user.Email = address.Trim(); user.NormalizedEmail = normalized;
        user.PasswordHash = hasher.HashPassword(user, password); user.UpdatedAt = DateTime.UtcNow;
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); }
        catch (DbUpdateException e) when (e.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 }) { return (null, "Este correo ya pertenece a una cuenta."); }
        return (user, null);
    }
    public async Task<User?> AuthenticateAsync(string address, string password) {
        var user = await FindAsync(address);
        var result = hasher.VerifyHashedPassword(user ?? dummy, user?.PasswordHash ?? dummyHash, password);
        if (user is null || user.PasswordHash is null || result == PasswordVerificationResult.Failed || !user.EmailVerified) return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded) {
            await using var db = await factory.CreateDbContextAsync();
            var tracked = await db.Users.SingleAsync(u => u.Id == user.Id); tracked.PasswordHash = hasher.HashPassword(tracked, password); await db.SaveChangesAsync();
        }
        return user;
    }
    public async Task<string> IssueTokenAsync(Guid userId, string purpose, TimeSpan? lifetime = null) {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.AccountTokens.Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, DateTime.UtcNow));
        var row = new AccountToken { UserId = userId, Purpose = purpose, ExpiresAt = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(1)) };
        db.AccountTokens.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync();
        return tokens.Protect($"{row.Id:N}:{userId:N}:{purpose}", new DateTimeOffset(row.ExpiresAt, TimeSpan.Zero));
    }
    private async Task<AccountToken?> ReadToken(AquarellaDbContext db, string value, string purpose) {
        try {
            var payload = tokens.Unprotect(value, out var expiry).Split(':');
            if (payload.Length != 3 || payload[2] != purpose || !Guid.TryParse(payload[0], out var id) || !Guid.TryParse(payload[1], out var userId) || expiry <= DateTimeOffset.UtcNow) return null;
            var row = await db.AccountTokens.SingleOrDefaultAsync(t => t.Id == id && t.UserId == userId && t.Purpose == purpose && t.UsedAt == null);
            return row is not null && row.ExpiresAt > DateTime.UtcNow ? row : null;
        } catch (CryptographicException) { return null; }
    }
    public async Task<bool> TokenValidAsync(string value, string purpose) { await using var db = await factory.CreateDbContextAsync(); return await ReadToken(db, value, purpose) is not null; }
    public async Task<bool> ConsumeAsync(string value, string purpose, string? password = null) {
        if (purpose == "reset" && (password is null || !ValidPassword(password))) return false;
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync();
        var token = await ReadToken(db, value, purpose); if (token is null) return false;
        var updated = await db.AccountTokens.Where(t => t.Id == token.Id && t.UsedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, DateTime.UtcNow));
        if (updated != 1) return false;
        var user = await db.Users.SingleAsync(u => u.Id == token.UserId);
        if (purpose == "verify") user.EmailVerified = true;
        else if (purpose == "reset") {
            user.PasswordHash = hasher.HashPassword(user, password!);
            await db.AccountLoginSessions.Where(s => s.UserId == user.Id).ExecuteDeleteAsync();
        } else return false;
        user.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(); await tx.CommitAsync(); return true;
    }
    public async Task SendAsync(User user, string purpose, string origin) {
        if (!email.Available) throw new InvalidOperationException("El proveedor de correo no está configurado.");
        var token = await IssueTokenAsync(user.Id, purpose);
        var path = purpose == "verify" ? "/verificar-correo" : "/nueva-contrasena";
        await email.SendAsync(user.Email!, purpose, $"{origin}{path}?token={Uri.EscapeDataString(token)}");
    }
    public async Task SignInAsync(HttpContext context, User user, bool remember) {
        await using var db = await factory.CreateDbContextAsync();
        var row = new AccountLoginSession { UserId = user.Id, ExpiresAt = DateTime.UtcNow.Add(remember ? TimeSpan.FromDays(14) : TimeSpan.FromHours(8)) };
        db.AccountLoginSessions.Add(row); await db.SaveChangesAsync();
        var claims = new List<Claim> { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, string.IsNullOrWhiteSpace(user.FirstName) ? user.Username : user.FirstName), new Claim("sid", row.Id.ToString()) };
        if (!string.IsNullOrWhiteSpace(user.Email)) claims.Add(new Claim(ClaimTypes.Email, user.Email));
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)), new AuthenticationProperties { IsPersistent = remember, ExpiresUtc = new DateTimeOffset(row.ExpiresAt, TimeSpan.Zero), AllowRefresh = false });
    }
    public async Task<bool> IsSessionValidAsync(ClaimsPrincipal principal) {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || !Guid.TryParse(principal.FindFirstValue("sid"), out var sessionId)) return false;
        await using var db = await factory.CreateDbContextAsync();
        // SQLite cannot order DateTimeOffset. UTC DateTime is used consistently.
        return await db.AccountLoginSessions.AnyAsync(s => s.Id == sessionId && s.UserId == userId && s.ExpiresAt > DateTime.UtcNow);
    }
    public async Task SignOutAsync(HttpContext context) {
        if (Guid.TryParse(context.User.FindFirstValue("sid"), out var id)) { await using var db = await factory.CreateDbContextAsync(); await db.AccountLoginSessions.Where(s => s.Id == id).ExecuteDeleteAsync(); }
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
