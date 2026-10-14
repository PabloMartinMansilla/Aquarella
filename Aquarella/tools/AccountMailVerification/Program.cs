using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Json;
using Aquarella.Data;
using Aquarella.Services.Accounts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

if (args.Length != 0 && (args.Length != 2 || args[0] != "restart")) throw new ArgumentException("No arguments, or restart <temporary fixture directory>.");
var root = args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "aquarella-mail-tests-" + Guid.NewGuid().ToString("N"));
if (!root.StartsWith(Path.Combine(Path.GetTempPath(), "aquarella-mail-tests-"), StringComparison.OrdinalIgnoreCase)) throw new Exception("Temporary fixtures only.");
Directory.CreateDirectory(root);
var factory = new Factory(new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite($"Data Source={Path.Combine(root, "test.db")}").Options);
var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root, "keys")), b => b.SetApplicationName("Aquarella.MailVerification"));
var sender = new FakeSender();
var log = new SafeLog();
var accounts = new AccountService(factory, protection, new AccountEmail(sender), logger: log);
const string password = "Disposable account password 123";
const string newPassword = "Disposable changed password 456";
const string origin = "https://aquarella.example.test";
var checks = 0;
void Check(bool valid, string name) { if (!valid) throw new Exception(name); checks++; }

if (args.Length == 2) {
    var token = await File.ReadAllTextAsync(Path.Combine(root, "restart-token.txt"));
    Check(await accounts.TokenValidAsync(token, "verify"), "Token survives process restart and persisted DP keys");
    Check(await accounts.ConsumeAsync(token, "verify"), "Restarted process can verify");
    Check(!await accounts.ConsumeAsync(token, "verify"), "Restart cannot reuse consumed token");
    Console.WriteLine("PASS child: persisted token and keys, verification, single use across process.");
    return;
}

// Upgrade an actual previous-schema fixture, including a pre-existing account and business.
Guid legacyId;
await using (var db = factory.CreateDbContext()) {
    await db.GetService<IMigrator>().MigrateAsync("20261005180000_StockIntakeReceipts");
    var user = new User { Username = "old-fixture", Email = "old@example.test", NormalizedEmail = "OLD@EXAMPLE.TEST" };
    var business = new Business { User = user, LegacyImported = true }; business.Profile.Name = "Preserved fixture";
    db.Businesses.Add(business); await db.SaveChangesAsync(); legacyId = user.Id;
    var oldId = Guid.NewGuid(); var expires = DateTime.UtcNow.AddHours(1);
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AccountTokens (Id,UserId,Purpose,ExpiresAt) VALUES ({oldId},{user.Id},{"verify"},{expires})");
    await db.Database.MigrateAsync();
    Check(!db.Database.HasPendingModelChanges(), "Migration snapshot exactly matches model");
    Check((await db.Businesses.SingleAsync()).Profile.Name == "Preserved fixture", "Upgrade preserves existing business");
    Check((await db.AccountTokens.SingleAsync()).CreatedAt == DateTime.MinValue, "Old token compatibility/cooldown default");
}
Check((await accounts.RegisterAsync("", "Test", "bad", "short")).User is null, "Invalid registration rejected server side");
var productionEnvironment = new FixtureEnvironment { EnvironmentName = "Production" };
var closedPolicy = new AccountRegistrationPolicy(new ConfigurationBuilder().Build(), productionEnvironment);
Check(!closedPolicy.Enabled, "Production registration defaults to private");
Check(new AccountRegistrationPolicy(new ConfigurationBuilder().Build(), new FixtureEnvironment()).Enabled, "Development registration remains enabled");
var openConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Accounts:Registration:Enabled"] = "true" }).Build();
Check(new AccountRegistrationPolicy(openConfig, productionEnvironment).Enabled, "Production registration can be explicitly reopened");
var privateAccounts = new AccountService(factory, protection, new AccountEmail(sender), environment: productionEnvironment, registration: closedPolicy);
Check((await privateAccounts.RegisterAsync("Private", "Fixture", "private@example.test", password)).Error == AccountRegistrationPolicy.ClosedMessage,
    "Service rejects registration even when email is available");
Check(await privateAccounts.FindAsync("private@example.test") is null && sender.Messages.Count == 0, "Private registration persists nothing and sends nothing");
var unavailableRegistration = new AccountService(factory, protection, new UnavailableAccountEmail(), environment: productionEnvironment,
    registration: new AccountRegistrationPolicy(openConfig, productionEnvironment));
Check((await unavailableRegistration.RegisterAsync("Private", "Fixture", "unavailable@example.test", password)).User is null,
    "Reopening Production registration cannot bypass unavailable email");
Check(await unavailableRegistration.FindAsync("unavailable@example.test") is null, "Unavailable email creates no account");
var a = (await accounts.RegisterAsync("Ana", "Fixture", "a@example.test", password)).User ?? throw new Exception("Registration A");
var b = (await accounts.RegisterAsync("Beto", "Fixture", "b@example.test", password)).User ?? throw new Exception("Registration B");
Check(!a.EmailVerified && !b.EmailVerified, "Registration pending");
Check(a.PasswordHash != password && a.PasswordHash is not null, "Password hashed");
Check(await accounts.AuthenticateAsync(a.Email!, password) is null, "Pending account cannot log in");
var pendingSession = new AccountLoginSession { UserId = b.Id, ExpiresAt = DateTime.UtcNow.AddHours(1) };
await using (var db = factory.CreateDbContext()) { db.AccountLoginSessions.Add(pendingSession); await db.SaveChangesAsync(); }
var pendingPrincipal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, b.Id.ToString()), new Claim("sid", pendingSession.Id.ToString())], "test"));
var productionAccounts = new AccountService(factory, protection, new AccountEmail(sender), environment: new FixtureEnvironment { EnvironmentName = "Production" });
Check(!await productionAccounts.IsSessionValidAsync(pendingPrincipal), "Production rejects a persisted Development session for an unverified account");
Check(await accounts.SendAsync(a, "verify", origin), "Verification request queued");
Check(sender.Messages.Count == 0, "Public request does not wait for/contact mail provider");
Check(!await accounts.SendAsync(a, "verify", origin), "Immediate resend throttled per account");
await accounts.ProcessPendingEmailsAsync();
Check(sender.Messages.Any(m => m.Purpose == "verify"), "Verification delivery failed: " + string.Join("; ", log.Messages));
var verification = sender.Messages.Last(m => m.Purpose == "verify").Link;
var verifyToken = Token(verification);
await using (var db = factory.CreateDbContext()) {
    var ticket = await db.AccountTokens.SingleAsync(t => t.UserId == a.Id && t.Purpose == "verify");
    Check(ticket.ExpiresAt - ticket.CreatedAt == TimeSpan.FromHours(24), "24h verification TTL");
    Check(!(await db.Users.SingleAsync(u => u.Id == a.Id)).EmailVerified, "Sending does not verify account");
}
Check(await accounts.TokenValidAsync(verifyToken, "verify"), "GET-equivalent read does not consume");
Check(await accounts.TokenValidAsync(verifyToken, "verify"), "Repeated scanner read is safe");
Check(!await accounts.ConsumeAsync(verifyToken, "reset", newPassword), "Verification cannot reset password");
await AgeRequests(a.Id, "verify");
Check(await accounts.SendAsync(a, "verify", origin), "Resend queues after cooldown");
Check(await accounts.TokenValidAsync(verifyToken, "verify"), "Existing link retained until delivery");
await accounts.ProcessPendingEmailsAsync();
Check(!await accounts.TokenValidAsync(verifyToken, "verify"), "Successful resend invalidates previous link");
verifyToken = Token(sender.Messages.Last(m => m.Purpose == "verify").Link);
Check(await accounts.ConsumeAsync(verifyToken, "verify"), "Explicit valid verification");
Check(!await accounts.ConsumeAsync(verifyToken, "verify"), "Verification is single use");
Check(!await accounts.SendAsync(a, "verify", origin), "Verified account receives no further verification");
Check(await accounts.AuthenticateAsync(a.Email!, password) is not null, "Verified account can log in");
Check(await accounts.AuthenticateAsync(a.Email!, password.ToUpperInvariant()) is null, "Case sensitive password retained");
await using (var db = factory.CreateDbContext()) { Check(!(await db.Users.SingleAsync(u => u.Id == b.Id)).EmailVerified, "A token never verifies B"); }

Check(await accounts.SendAsync(a, "reset", origin), "Recovery queued");
await accounts.ProcessPendingEmailsAsync();
var reset = Token(sender.Messages.Last(m => m.Purpose == "reset").Link);
await using (var db = factory.CreateDbContext()) {
    var row = await db.AccountTokens.SingleAsync(t => t.UserId == a.Id && t.Purpose == "reset");
    Check(row.ExpiresAt - row.CreatedAt == TimeSpan.FromMinutes(30), "30m recovery TTL");
}
Check(!await accounts.ConsumeAsync(reset, "reset", "short"), "Invalid new password rejected");
Check(await accounts.TokenValidAsync(reset, "reset"), "Bad password does not consume link");
var sid = Guid.NewGuid();
await using (var db = factory.CreateDbContext()) { db.AccountLoginSessions.Add(new() { Id = sid, UserId = a.Id, ExpiresAt = DateTime.UtcNow.AddHours(1) }); await db.SaveChangesAsync(); }
var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, a.Id.ToString()), new Claim("sid", sid.ToString())], "test"));
Check(await accounts.IsSessionValidAsync(principal), "Old session initially valid");
sender.Fail = true;
Check(await accounts.ConsumeAsync(reset, "reset", newPassword), "Password commit succeeds despite notification failure");
Check(!await accounts.IsSessionValidAsync(principal), "All previous sessions revoked");
Check(!await accounts.ConsumeAsync(reset, "reset", password), "Recovery single use");
Check(await accounts.AuthenticateAsync(a.Email!, newPassword) is not null && await accounts.AuthenticateAsync(a.Email!, password) is null, "Only new password accepted");
Check(await accounts.AuthenticateAsync(b.Email!, newPassword) is null, "A reset does not modify B");
await using (var db = factory.CreateDbContext()) {
    var row = await db.AccountEmailNotifications.SingleAsync(n => n.UserId == a.Id && n.Purpose == "password-changed");
    Check(row.SentAt is null && row.Attempts == 1, "Failure retains durable notification");
    Check(row.TokenId is null && row.PublicOrigin is null, "Notification persists no secret/link");
}
sender.Fail = false;
Check(await accounts.RetryPasswordNotificationsAsync(a.Id) == 0, "Immediate retry throttled");
await AgeNotifications();
var restarted = new AccountService(factory, DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root, "keys")), x => x.SetApplicationName("Aquarella.MailVerification")), new AccountEmail(sender), logger: log);
await Task.WhenAll(restarted.RetryPasswordNotificationsAsync(a.Id), accounts.RetryPasswordNotificationsAsync(a.Id));
Check(sender.Messages.Count(m => m.Purpose == "password-changed") == 1, "Concurrent retries send once");
var notification = sender.Messages.Single(m => m.Purpose == "password-changed");
Check(!notification.Body.Contains(password) && !notification.Body.Contains(newPassword) && notification.Link == "", "Security notification has no password or token");
Check(await accounts.RetryPasswordNotificationsAsync(a.Id) == 0, "Delivered notification is not repeated");

Check(!await accounts.TokenValidAsync("tampered-token", "reset") && !await accounts.TokenValidAsync(new string('x', 5000), "reset"), "Malformed/oversized token safe");
foreach (var purpose in new[] { "verify", "reset" }) {
    var expired = await accounts.IssueTokenAsync(b.Id, purpose, TimeSpan.FromSeconds(-1));
    Check(!await accounts.ConsumeAsync(expired, purpose, newPassword), "Expired " + purpose + " rejected");
}
// Failed token delivery neither disables an old delivered link nor leaves the failed new link usable.
await AgeRequests(a.Id, "reset");
await accounts.SendAsync(a, "reset", origin); await accounts.ProcessPendingEmailsAsync();
var previous = Token(sender.Messages.Last(m => m.Purpose == "reset").Link);
await AgeRequests(a.Id, "reset"); sender.Fail = true;
Check(await accounts.SendAsync(a, "reset", origin), "New recovery request accepted independently of provider");
await accounts.ProcessPendingEmailsAsync(); sender.Fail = false;
Check(await accounts.TokenValidAsync(previous, "reset"), "Failed send preserves previous delivered link");
await using (var db = factory.CreateDbContext()) {
    var failed = await db.AccountEmailNotifications.SingleAsync(n => n.UserId == a.Id && n.Purpose == "reset" && n.CancelledAt != null);
    Check((await db.AccountTokens.SingleAsync(t => t.Id == failed.TokenId)).UsedAt != null, "Failed token is revoked");
}
Check(log.Messages.All(x => !x.Contains("SECRET") && !x.Contains("token=") && !x.Contains("@") && !x.Contains(password)), "Logs redact even sensitive provider exception messages");

// A failing database write must roll back token consumption, session revocation and queued notification.
var rollbackSid = Guid.NewGuid();
await using (var db = factory.CreateDbContext()) { db.AccountLoginSessions.Add(new() { Id = rollbackSid, UserId = a.Id, ExpiresAt = DateTime.UtcNow.AddHours(1) }); await db.SaveChangesAsync(); }
var failingFactory = new Factory(new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite($"Data Source={Path.Combine(root, "test.db")}").AddInterceptors(new PasswordFailure()).Options);
var failingAccounts = new AccountService(failingFactory, protection, new AccountEmail(sender));
try { await failingAccounts.ConsumeAsync(previous, "reset", password); throw new Exception("Injected persistence failure missing"); } catch (DbUpdateException) { checks++; }
Check(await accounts.TokenValidAsync(previous, "reset"), "Rollback preserves reset token for retry");
Check(await accounts.AuthenticateAsync(a.Email!, newPassword) is not null, "Rollback preserves old hash");
await using (var db = factory.CreateDbContext()) { Check(await db.AccountLoginSessions.AnyAsync(x => x.Id == rollbackSid), "Rollback preserves sessions"); }

var limitedUser = (await accounts.RegisterAsync("Limit", "Fixture", "limited@example.test", password)).User!;
for (var i = 0; i < 5; i++) {
    if (i > 0) await AgeRequests(limitedUser.Id, "verify");
    Check(await accounts.SendAsync(limitedUser, "verify", origin), "Per-account hourly budget permits request " + i);
}
await AgeRequests(limitedUser.Id, "verify");
Check(!await accounts.SendAsync(limitedUser, "verify", origin), "Hourly account limit blocks sixth request even across cooldowns");
// Durable notification retries are bounded; manipulated fixture times avoid sleeping five minutes.
var limitedReset = await accounts.IssueTokenAsync(limitedUser.Id, "reset"); sender.Fail = true;
Check(await accounts.ConsumeAsync(limitedReset, "reset", newPassword), "Changed password stays committed while delivery fails");
for (var i = 0; i < 5; i++) { await AgeNotifications(); await accounts.RetryPasswordNotificationsAsync(limitedUser.Id); }
await using (var db = factory.CreateDbContext()) {
    var row = await db.AccountEmailNotifications.SingleAsync(n => n.UserId == limitedUser.Id && n.Purpose == "password-changed");
    Check(row.Attempts == 5 && row.SentAt is null, "Notification retries stop at five and retain operational evidence");
}
sender.Fail = false;

// Per-user reservation is shared between service instances; simultaneous requests are deterministic.
await AgeRequests(b.Id, "verify");
var concurrent = await Task.WhenAll(accounts.SendAsync(b, "verify", origin), restarted.SendAsync(b, "verify", origin));
Check(concurrent.Count(x => x) == 1, "Concurrent send reservations cannot spam");
await accounts.ProcessPendingEmailsAsync();
var bToken = Token(sender.Messages.Last(m => m.Recipient == b.Email && m.Purpose == "verify").Link);
await File.WriteAllTextAsync(Path.Combine(root, "restart-token.txt"), bToken);
var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
start.ArgumentList.Add(typeof(Factory).Assembly.Location); start.ArgumentList.Add("restart"); start.ArgumentList.Add(root);
using (var child = Process.Start(start)!) {
    var output = await child.StandardOutput.ReadToEndAsync(); var error = await child.StandardError.ReadToEndAsync();
    await child.WaitForExitAsync(); Check(child.ExitCode == 0, "Separate process restart verifier: " + error); Console.Write(output);
}
Check(!await accounts.TokenValidAsync(bToken, "verify"), "Parent observes child consumption in fresh context");

foreach (var invalidOrigin in new[] { "http://example.test", "https://localhost", "https://127.0.0.1", "https://example.test/other", "https://user@example.test", "https://example.test?x=1" }) {
    try { AccountMailOrigin.Validate(invalidOrigin, false); throw new Exception("Unsafe origin accepted"); } catch (InvalidOperationException) { checks++; }
}
Check(AccountMailOrigin.Validate("https://localhost:7188", true) == "https://localhost:7188", "Development HTTPS allowed explicitly");
var unavailable = new SmtpEmailSender(Options.Create(new SmtpEmailOptions()));
Check(!unavailable.Available, "Unconfigured SMTP unavailable");
try { await unavailable.SendAsync(new("a@example.test", "Fixture", "Fixture")); throw new Exception("Unconfigured send accepted"); } catch (InvalidOperationException) { checks++; }
foreach (var security in new[] { "None", "Auto", "SslOnConnect" }) {
    var option = Config(); option.Security = security; Check(!new SmtpEmailSender(Options.Create(option)).Available, "Insecure/unsupported TLS refused");
}
var implicitTls = Config(); implicitTls.Port = 465; Check(!implicitTls.IsConfigured, "Implicit TLS cannot accidentally become plaintext STARTTLS");
try { new AccountMailOptions { RecoveryMinutes = 0 }.Validate(); throw new Exception("Bad TTL accepted"); } catch (OptionsValidationException) { checks++; }
// Loopback-only fake SMTP advertises no STARTTLS: AUTH must never be sent.
using (var tcp = new TcpListener(IPAddress.Loopback, 0)) {
    tcp.Start(); var cfg = Config(); cfg.Host = "127.0.0.1"; cfg.Port = ((IPEndPoint)tcp.LocalEndpoint).Port; cfg.TimeoutSeconds = 2;
    var lines = new List<string>();
    var server = Task.Run(async () => {
        using var connection = await tcp.AcceptTcpClientAsync(); await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream); await using var writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\r\n" };
        await writer.WriteLineAsync("220 local fixture");
        var greeting = await reader.ReadLineAsync(); lines.Add(greeting ?? "");
        await writer.WriteLineAsync("250 local fixture");
        try { var next = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)); if (next is not null) lines.Add(next); } catch (IOException) { }
    });
    try { await new SmtpEmailSender(Options.Create(cfg)).SendAsync(new("a@example.test", "Fixture", "Fixture")); throw new Exception("Non-TLS SMTP accepted"); } catch (IOException) { checks++; }
    await server.WaitAsync(TimeSpan.FromSeconds(5));
    Check(lines.All(line => !line.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase) && !line.Contains(cfg.Password)), "No credentials sent to SMTP without STARTTLS");
}
using (var tcp = new TcpListener(IPAddress.Loopback, 0)) {
    tcp.Start(); var cfg = Config(); cfg.Host = "127.0.0.1"; cfg.Port = ((IPEndPoint)tcp.LocalEndpoint).Port; cfg.TimeoutSeconds = 1;
    var server = Task.Run(async () => { using var connection = await tcp.AcceptTcpClientAsync(); await Task.Delay(1500); });
    try { await new SmtpEmailSender(Options.Create(cfg)).SendAsync(new("a@example.test", "Fixture", "Fixture")); throw new Exception("SMTP timeout not enforced"); }
    catch (IOException e) { Check(!e.Message.Contains(cfg.Password) && e.InnerException is null, "Timeout returns sanitized, controlled failure"); }
    await server.WaitAsync(TimeSpan.FromSeconds(4));
}
using (var tcp = new TcpListener(IPAddress.Loopback, 0)) {
    tcp.Start(); var cfg = Config(); cfg.Host = "127.0.0.1"; cfg.Port = ((IPEndPoint)tcp.LocalEndpoint).Port; tcp.Stop();
    try { await new SmtpEmailSender(Options.Create(cfg)).SendAsync(new("a@example.test", "Fixture", "Fixture")); throw new Exception("Rejected SMTP connection accepted"); }
    catch (IOException e) { Check(e.InnerException is null, "Rejected connection returns no sensitive provider details"); }
}
var env = new FixtureEnvironment { ContentRootPath = root };
await new DevelopmentEmailSender(env).SendAsync(AccountEmail.Content("fake@example.test", "reset", "https://aquarella.example.test/fake"));
Check(Directory.GetFiles(Path.Combine(root, "App_Data", "development-emails")).Length == 1, "Private simulated email includes content");
env.EnvironmentName = "Production";
try { await new DevelopmentEmailSender(env).SendAsync(new("fake@example.test", "Fixture", "Fixture")); throw new Exception("Simulator enabled in Production"); } catch (InvalidOperationException) { checks++; }
Console.WriteLine($"PASS mail: {checks} checks; migration compatibility, register/verify/reset, expiry, purposes/ownership, cooldown/leases, failure/retry, restart, SMTP/TLS, private simulator, sanitized logging.");
Console.WriteLine("Temporary artifacts: " + root);

async Task AgeRequests(Guid user, string purpose) {
    await using var db = await factory.CreateDbContextAsync();
    await db.AccountTokens.Where(t => t.UserId == user && t.Purpose == purpose).ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, DateTime.UtcNow.AddMinutes(-2)));
}
async Task AgeNotifications() {
    await using var db = await factory.CreateDbContextAsync();
    await db.AccountEmailNotifications.Where(n => n.SentAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.LastAttemptAt, DateTime.UtcNow.AddMinutes(-10)).SetProperty(n => n.LeaseUntil, DateTime.UtcNow.AddMinutes(-10)));
}
static string Token(string link) => Uri.UnescapeDataString(new Uri(link).Query[7..]);
static SmtpEmailOptions Config() => new() { Enabled = true, Host = "smtp.invalid", Username = "fake-fixture", Password = "SECRET-fixture-only", FromAddress = "fake@example.test" };
sealed class Factory(DbContextOptions<AquarellaDbContext> options) : IDbContextFactory<AquarellaDbContext> {
    public AquarellaDbContext CreateDbContext() => new(options);
    public Task<AquarellaDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
}
sealed class FakeSender : IEmailSender {
    public bool Available => true;
    public bool Fail { get; set; }
    public System.Collections.Concurrent.ConcurrentQueue<EmailMessage> Messages { get; } = new();
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) {
        if (Fail) throw new IOException("SECRET provider fixture: token=private, fake@example.test");
        Messages.Enqueue(message); return Task.CompletedTask;
    }
}
sealed class SafeLog : ILogger<AccountService> {
    public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Enqueue(formatter(state, exception));
}
sealed class PasswordFailure : SaveChangesInterceptor {
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) {
        if (eventData.Context!.ChangeTracker.Entries<User>().Any(e => e.State == EntityState.Modified && e.Property(u => u.PasswordHash).IsModified))
            throw new DbUpdateException("Controlled password persistence failure");
        return ValueTask.FromResult(result);
    }
}
sealed class FixtureEnvironment : IWebHostEnvironment {
    public string ApplicationName { get; set; } = "Fixture";
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
