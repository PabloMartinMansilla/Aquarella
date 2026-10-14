using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using Aquarella.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aquarella.Services.Accounts;

public sealed class AccountService(IDbContextFactory<AquarellaDbContext> factory, IDataProtectionProvider protection, IAccountEmail email,
    IOptions<AccountMailOptions>? configured = null, ILogger<AccountService>? logger = null, IWebHostEnvironment? environment = null,
    AccountRegistrationPolicy? registration = null)
{
    private readonly AccountMailOptions mail = CheckedOptions(configured);
    private readonly ILogger<AccountService> log = logger ?? NullLogger<AccountService>.Instance;
    private static AccountMailOptions CheckedOptions(IOptions<AccountMailOptions>? options) { var value = options?.Value ?? new(); value.Validate(); return value; }
    private readonly PasswordHasher<User> hasher = new();
    private readonly ITimeLimitedDataProtector tokens = protection.CreateProtector("Aquarella.AccountTokens.v1").ToTimeLimitedDataProtector();
    private static readonly User dummy = new();
    private static readonly string dummyHash = new PasswordHasher<User>().HashPassword(dummy, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    public static string Normalize(string email) => email.Trim().ToUpperInvariant();
    public static bool ValidEmail(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 254 && new EmailAddressAttribute().IsValid(value.Trim());
    public static bool ValidPassword(string? value) => value is { Length: >= 12 and <= 256 };
    public async Task<User?> FindAsync(string address) {
        await using var db = await factory.CreateDbContextAsync(); var normalized = Normalize(address);
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.NormalizedEmail == normalized);
    }
    public async Task<(User? User, string? Error)> RegisterAsync(string first, string last, string address, string password, string? legacyToken = null) {
        if (!(registration?.Enabled ?? environment?.IsDevelopment() ?? true)) return (null, AccountRegistrationPolicy.ClosedMessage);
        if (environment is not null && !environment.IsDevelopment() && !email.Available)
            return (null, "El servicio de correo todavía no está disponible.");
        if (string.IsNullOrWhiteSpace(first) || first.Trim().Length > 100 || string.IsNullOrWhiteSpace(last) || last.Trim().Length > 100 || !ValidEmail(address) || !ValidPassword(password)) return (null, "Datos de cuenta inválidos.");
        await using var db = await factory.CreateDbContextAsync(); await using var tx = await db.Database.BeginTransactionAsync();
        var normalized = Normalize(address);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized)) {
            // Keep the expensive password work comparable to a new registration; UI remains generic.
            hasher.HashPassword(dummy, password);
            return (null, "Este correo ya pertenece a una cuenta.");
        }
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
        var row = new AccountToken { UserId = userId, Purpose = purpose, ExpiresAt = DateTime.UtcNow.Add(lifetime ?? mail.Lifetime(purpose)) };
        db.AccountTokens.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync();
        return tokens.Protect($"{row.Id:N}:{userId:N}:{purpose}", new DateTimeOffset(row.ExpiresAt, TimeSpan.Zero));
    }
    private async Task<AccountToken?> ReadToken(AquarellaDbContext db, string value, string purpose) {
        if (string.IsNullOrEmpty(value) || value.Length > 4096) return null;
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
        AccountEmailNotification? notification = null;
        if (purpose == "verify") { if (user.EmailVerified) return false; user.EmailVerified = true; }
        else if (purpose == "reset") {
            user.PasswordHash = hasher.HashPassword(user, password!);
            await db.AccountLoginSessions.Where(s => s.UserId == user.Id).ExecuteDeleteAsync();
            notification = new AccountEmailNotification { UserId = user.Id };
            db.AccountEmailNotifications.Add(notification);
        } else return false;
        await db.AccountTokens.Where(t => t.UserId == user.Id && t.Purpose == purpose && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, DateTime.UtcNow));
        user.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(); await tx.CommitAsync();
        // Password, token consumption, session revocation and notification are committed together.
        // Delivery cannot roll back a completed change, even if the provider/DB is temporarily unavailable.
        if (notification is not null) await DeliverNotificationSafely(notification.Id, user.Id);
        return true;
    }
    public async Task<bool> SendAsync(User user, string purpose, string origin) {
        if (!email.Available) throw new InvalidOperationException("El proveedor de correo no está configurado.");
        if (purpose is not ("verify" or "reset")) throw new ArgumentException("Propósito no válido.", nameof(purpose));
        origin = AccountMailOrigin.Validate(origin, environment?.IsDevelopment() == true);
        var now = DateTime.UtcNow;
        var cooldown = now.AddSeconds(-mail.ResendSeconds);
        var hour = now.AddHours(-1);
        await using (var db = await factory.CreateDbContextAsync()) {
            // SQLite's immediate transaction serializes reservations, including separate server instances.
            await using var tx = await db.Database.BeginTransactionAsync();
            var current = await db.Users.SingleOrDefaultAsync(u => u.Id == user.Id);
            if (current?.Email is null || current.PasswordHash is null || (purpose == "verify" && current.EmailVerified)) return false;
            var recent = db.AccountTokens.Where(t => t.UserId == user.Id && t.Purpose == purpose);
            if (await recent.AnyAsync(t => t.CreatedAt > cooldown) || await recent.CountAsync(t => t.CreatedAt > hour) >= mail.SendsPerHour) return false;
            var row = new AccountToken { UserId = user.Id, Purpose = purpose, CreatedAt = now, ExpiresAt = now.Add(mail.Lifetime(purpose)) };
            db.AccountTokens.Add(row);
            db.AccountEmailNotifications.Add(new AccountEmailNotification { UserId = user.Id, Purpose = purpose, TokenId = row.Id, PublicOrigin = origin });
            await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        // Queue acceptance is not delivery confirmation. SMTP latency is absent from public responses.
        return true;
    }
    public async Task ProcessPendingEmailsAsync(CancellationToken stoppingToken = default) {
        await using var db = await factory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var retryAfter = now.AddMinutes(-mail.NotificationRetryMinutes);
        var work = await db.AccountEmailNotifications.Where(n => n.SentAt == null && n.CancelledAt == null && n.Attempts < 5 &&
            (n.LastAttemptAt == null || n.LastAttemptAt <= retryAfter) && (n.LeaseUntil == null || n.LeaseUntil <= now))
            .OrderBy(n => n.CreatedAt).Select(n => new { n.Id, n.UserId }).Take(20).ToListAsync();
        foreach (var item in work) {
            if (stoppingToken.IsCancellationRequested) break;
            await DeliverNotificationSafely(item.Id, item.UserId);
        }
    }
    public async Task<int> RetryPasswordNotificationsAsync(Guid userId) {
        await using var db = await factory.CreateDbContextAsync();
        var ids = await db.AccountEmailNotifications.Where(n => n.UserId == userId && n.Purpose == "password-changed" && n.SentAt == null && n.CancelledAt == null && n.Attempts < 5)
            .OrderBy(n => n.CreatedAt).Select(n => n.Id).Take(3).ToListAsync();
        var sent = 0;
        foreach (var id in ids) if (await DeliverNotificationSafely(id, userId)) sent++;
        return sent;
    }
    private async Task<bool> DeliverNotificationSafely(Guid id, Guid userId) {
        try { return await DeliverNotification(id, userId); }
        catch (Exception e) {
            log.LogWarning("La notificación de seguridad {NotificationId} permanece pendiente ({Type}).", id, e.GetType().Name);
            return false;
        }
    }
    private async Task<bool> DeliverNotification(Guid id, Guid userId) {
        if (!email.Available) return false;
        var now = DateTime.UtcNow;
        var retryAfter = now.AddMinutes(-mail.NotificationRetryMinutes);
        await using var db = await factory.CreateDbContextAsync();
        var claimed = await db.AccountEmailNotifications.Where(n => n.Id == id && n.UserId == userId && n.SentAt == null && n.CancelledAt == null && n.Attempts < 5 &&
                (n.LastAttemptAt == null || n.LastAttemptAt <= retryAfter) && (n.LeaseUntil == null || n.LeaseUntil <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Attempts, n => n.Attempts + 1).SetProperty(n => n.LastAttemptAt, now).SetProperty(n => n.LeaseUntil, now.AddMinutes(2)));
        if (claimed != 1) return false;
        var job = await db.AccountEmailNotifications.AsNoTracking().SingleAsync(n => n.Id == id && n.UserId == userId);
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        AccountToken? ticket = null;
        var link = "";
        if (job.Purpose is "verify" or "reset") {
            ticket = await db.AccountTokens.AsNoTracking().SingleOrDefaultAsync(t => t.Id == job.TokenId && t.UserId == userId && t.Purpose == job.Purpose && t.UsedAt == null && t.ExpiresAt > now);
            if (ticket is null || (job.Purpose == "verify" && user.EmailVerified)) {
                await db.AccountEmailNotifications.Where(n => n.Id == id).ExecuteUpdateAsync(s => s.SetProperty(n => n.CancelledAt, now));
                return false;
            }
            var origin = AccountMailOrigin.Validate(job.PublicOrigin, environment?.IsDevelopment() == true);
            var token = tokens.Protect($"{ticket.Id:N}:{userId:N}:{job.Purpose}", new DateTimeOffset(ticket.ExpiresAt, TimeSpan.Zero));
            var path = job.Purpose == "verify" ? "/verificar-correo" : "/nueva-contrasena";
            link = $"{origin}{path}?token={Uri.EscapeDataString(token)}";
        }
        try { await email.SendAsync(user.Email!, job.Purpose, link); }
        catch (Exception e) when (e is IOException or InvalidOperationException) {
            // A failed token delivery is terminal; a fresh user request can retry after the cooldown.
            // Preserve previously delivered links. Password notifications retain their durable retry.
            if (ticket is not null) {
                await using var tx = await db.Database.BeginTransactionAsync();
                await db.AccountTokens.Where(t => t.Id == ticket.Id && t.UsedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, DateTime.UtcNow));
                await db.AccountEmailNotifications.Where(n => n.Id == id).ExecuteUpdateAsync(s => s.SetProperty(n => n.CancelledAt, DateTime.UtcNow));
                await tx.CommitAsync();
            }
            throw;
        }
        if (ticket is not null) {
            // A late send must not invalidate a newer request.
            await db.AccountTokens.Where(t => t.UserId == userId && t.Purpose == job.Purpose && t.Id != ticket.Id && t.CreatedAt <= ticket.CreatedAt && t.UsedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, DateTime.UtcNow));
        }
        await db.AccountEmailNotifications.Where(n => n.Id == id && n.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.SentAt, DateTime.UtcNow).SetProperty(n => n.LeaseUntil, (DateTime?)null));
        return true;
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
        if (principal.Identity?.IsAuthenticated != true) return false;
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || !Guid.TryParse(principal.FindFirstValue("sid"), out var sessionId)) return false;
        await using var db = await factory.CreateDbContextAsync();
        // SQLite cannot order DateTimeOffset. UTC DateTime is used consistently.
        var requireVerified = environment is not null && !environment.IsDevelopment();
        return await db.AccountLoginSessions.AnyAsync(s => s.Id == sessionId && s.UserId == userId && s.ExpiresAt > DateTime.UtcNow &&
            (!requireVerified || db.Users.Any(u => u.Id == userId && u.EmailVerified && u.PasswordHash != null)));
    }
    public async Task SignOutAsync(HttpContext context) {
        if (context.User.Identity?.IsAuthenticated == true && Guid.TryParse(context.User.FindFirstValue("sid"), out var id) && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) {
            await using var db = await factory.CreateDbContextAsync();
            await db.AccountLoginSessions.Where(s => s.Id == id && s.UserId == userId).ExecuteDeleteAsync();
        }
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
