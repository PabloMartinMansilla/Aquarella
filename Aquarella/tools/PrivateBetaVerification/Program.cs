using Aquarella.Data;
using Aquarella.Services.Accounts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using System.Text.Json;

// Reproducible preparation procedure, not a provisioning tool or an authentication bypass.
// Optional maintenance executable only; never accepts a database/account supplied by the operator.
if (args.Length > 1 || args.Length == 1 && (!Path.IsPathFullyQualified(args[0])
    || Path.GetFileName(args[0]) != "DatabaseMaintenance.dll" || !File.Exists(args[0])))
    throw new ArgumentException("No arguments, or the absolute published DatabaseMaintenance.dll path.");
var root = Path.Combine(Path.GetTempPath(), "aquarella-private-beta-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var source = Path.Combine(root, "preparation.db");
var backup = Path.Combine(root, "backup.db");
var restored = Path.Combine(root, "restored.db");
await DatabaseMaintenance.ApplyAsync(source, Path.Combine(root, "receipts"), confirmStopped: true, allowCreate: true);
var empty = await PrivateBetaReadiness.InspectAsync(source);
if (empty.Ready || empty.VerifiedUsers != 0) throw new Exception("Empty beta must not be ready");
var factory = new Factory(source);
var development = new FixtureEnvironment { EnvironmentName = "Development" };
var production = new FixtureEnvironment { EnvironmentName = "Production" };
var config = new ConfigurationBuilder().Build();
var transport = new FixtureEmail();
var localProtection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root, "local-keys")));
var local = new AccountService(factory, localProtection, transport, environment: development,
    registration: new AccountRegistrationPolicy(config, development));
const string password = "Disposable private beta fixture 123!";
var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
var owner = (await local.RegisterAsync("Beta", "Fixture", "private-beta@example.test", password)).User
    ?? throw new Exception("Fixture registration failed");
Check(!owner.EmailVerified && owner.PasswordHash is not null && owner.PasswordHash != password, "normal unverified registration with existing password hasher");
Check(!(await PrivateBetaReadiness.InspectAsync(source)).Ready, "pending verification blocks beta preflight");
Check(await local.AuthenticateAsync(owner.Email!, password) is null, "verification is required before login");
Check(await local.SendAsync(owner, "verify", "https://localhost:7188"), "verification request uses the existing queue");
var queued = await PrivateBetaReadiness.InspectAsync(source);
Check(!queued.Ready && queued.UsableTokens == 1 && queued.PendingEmails == 1, "preflight identifies pending token and email");
await local.ProcessPendingEmailsAsync();
Check(transport.Links.Count == 1, "one verification delivered to the in-memory fixture only");
var token = Uri.UnescapeDataString(new Uri(transport.Links.Single()).Query[7..]);
Check(await local.ConsumeAsync(token, "verify"), "existing single-use verification flow");
Check(!await local.ConsumeAsync(token, "verify"), "verification cannot be replayed");
Check(await local.AuthenticateAsync(owner.Email!, password) is not null, "verified credentials work");
await using (var db = factory.CreateDbContext()) {
    Check(await db.Users.CountAsync() == 1 && await db.Businesses.CountAsync() == 1, "only an explicitly registered fixture and owned business exist");
    Check(!await db.AccountLoginSessions.AnyAsync(), "preparation exports no login session");
    Check(!await db.AccountTokens.AnyAsync(t => t.UsedAt == null), "no usable tokens are exported");
    Check(!await db.AccountEmailNotifications.AnyAsync(n => n.SentAt == null && n.CancelledAt == null), "no pending emails exported");
}
Check((await PrivateBetaReadiness.InspectAsync(source)).Ready, "verified session-free preparation is ready");
await using (var db = factory.CreateDbContext()) {
    db.AccountLoginSessions.Add(new() { UserId = owner.Id, ExpiresAt = DateTime.UtcNow.AddHours(1) });
    await db.SaveChangesAsync();
}
var liveSession = await PrivateBetaReadiness.InspectAsync(source);
Check(!liveSession.Ready && liveSession.ActiveSessions == 1, "preflight rejects an active login session");
// Only a fictitious row in this test. Operators must log out through the existing UI.
await using (var db = factory.CreateDbContext()) { await db.AccountLoginSessions.ExecuteDeleteAsync(); }
await using (var db = factory.CreateDbContext()) {
    db.Users.Add(new User { Username = "incomplete-fixture", EmailVerified = true });
    await db.SaveChangesAsync();
}
Check((await PrivateBetaReadiness.InspectAsync(source)) is { Ready: false, IncompleteAccounts: 1 }, "preflight rejects incomplete/legacy accounts");
await using (var db = factory.CreateDbContext()) { await db.Users.Where(u => u.Username == "incomplete-fixture").ExecuteDeleteAsync(); }
await using (var reader = DatabaseMigrationSafety.Openable(source, Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly)) {
    await reader.OpenAsync();
    var before = await DatabaseMigrationSafety.DataAsync(reader);
    Check((await PrivateBetaReadiness.InspectAsync(source)).Ready, "preflight is ready after normal fixture cleanup");
    DatabaseMigrationSafety.RequireSameData(before, await DatabaseMigrationSafety.DataAsync(reader));
}
var missing = Path.Combine(root, "missing.db");
try { await PrivateBetaReadiness.InspectAsync(missing); throw new Exception("Missing beta accepted"); }
catch (InvalidOperationException) { Check(!File.Exists(missing), "missing beta is rejected without creating a database"); }
await DatabaseMaintenance.BackupAsync(source, backup);
await DatabaseMaintenance.RestoreToNewFileAsync(backup, restored);
await DatabaseMigrationSafety.RequireCurrentAsync(restored);
Check((await PrivateBetaReadiness.InspectAsync(restored)).Ready, "restored beta passes the same readiness check");
var freshProtection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root, "production-keys")));
var privateAccounts = new AccountService(new Factory(restored), freshProtection, new NoEmail(), environment: production,
    registration: new AccountRegistrationPolicy(config, production));
var login = await privateAccounts.AuthenticateAsync(owner.Email!, password);
Check(login is { EmailVerified: true } && login.Id == owner.Id && login.PasswordHash == owner.PasswordHash,
    "verified account survives SQLite backup/restore with a separate Production key ring");
Check(await privateAccounts.AuthenticateAsync(owner.Email!, password + "invalid") is null, "incorrect password remains rejected");
Check((await privateAccounts.RegisterAsync("Other", "Fixture", "other@example.test", password)).User is null,
    "Production registration stays closed after restoring verified accounts");
Check(!await privateAccounts.TokenValidAsync(token, "verify"), "Development token is unusable with fresh Production protection");
await using (var db = new Factory(restored).CreateDbContext()) {
    Check(await db.Users.CountAsync() == 1 && await db.Businesses.CountAsync() == 1, "closed registration adds nothing");
}
if (args.Length == 1) {
    var ready = await Cli("inspect-private-beta", "--database", restored);
    Check(ready.ExitCode == 0 && JsonSerializer.Deserialize<PrivateBetaAssessment>(ready.Output) is { Ready: true, VerifiedUsers: 1 },
        "published standalone CLI runs the read-only preflight");
    Check(!ready.Output.Contains(owner.Email!) && !ready.Output.Contains(owner.PasswordHash!) && !ready.Output.Contains(password),
        "CLI returns no personal data or credentials");
    var refused = await Cli("inspect-private-beta", "--database", missing, "--create");
    Check(refused.ExitCode == 1 && !File.Exists(missing), "CLI rejects a write flag without creating the missing database");
    var absent = await Cli("inspect-private-beta", "--database", missing);
    Check(absent.ExitCode == 1 && !File.Exists(missing), "CLI safely rejects a missing database");
    await using (var db = new Factory(restored).CreateDbContext()) {
        db.AccountLoginSessions.Add(new() { UserId = owner.Id, ExpiresAt = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();
    }
    var notReady = await Cli("inspect-private-beta", "--database", restored);
    Check(notReady.ExitCode == 2 && JsonSerializer.Deserialize<PrivateBetaAssessment>(notReady.Output) is { Ready: false, ActiveSessions: 1 },
        "CLI rejects an active session using exit code 2");
    async Task<(int ExitCode, string Output)> Cli(params string[] arguments) {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(args[0]);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        _ = await errors;
        return (process.ExitCode, await output);
    }
}
Console.WriteLine($"PASS private beta: {checks} checks; registration/queue/verification/hash, verified backup/restore, separate key ring, closed Production; no real accounts or email.");

sealed class Factory(string path) : IDbContextFactory<AquarellaDbContext> {
    public AquarellaDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AquarellaDbContext>()
        .UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite, Pooling = false }.ToString()).Options);
}
sealed class FixtureEmail : IAccountEmail {
    public bool Available => true;
    public List<string> Links { get; } = [];
    public Task SendAsync(string recipient, string purpose, string link) { Links.Add(link); return Task.CompletedTask; }
}
sealed class NoEmail : IAccountEmail {
    public bool Available => false;
    public Task SendAsync(string recipient, string purpose, string link) => throw new Exception("No email should be sent in Production");
}
sealed class FixtureEnvironment : IWebHostEnvironment {
    public string ApplicationName { get; set; } = "Fixture";
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = "";
    public string WebRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
