using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aquarella.Data;
using Aquarella.Services.Accounts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

var root = Path.Combine(Path.GetTempPath(), "aquarella-migration-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
string PathFor(string name) => Path.Combine(root, name + ".db");
string Backups(string name) => Path.Combine(root, name + "-backups");
AquarellaDbContext Context(string path) => new(new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);
async Task Reject(Func<Task> action, string name) {
    try { await action(); throw new Exception("Expected refusal: " + name); } catch (Exception e) when (e is InvalidOperationException or IOException) { checks++; }
}
async Task<DatabaseDataStamp> Stamp(string path, DatabaseDataStamp? original = null) {
    await using var connection = DatabaseMigrationSafety.Openable(path, SqliteOpenMode.ReadOnly); await connection.OpenAsync();
    return await DatabaseMigrationSafety.DataAsync(connection, original: original);
}
var missing = PathFor("missing");
Check((await DatabaseMigrationSafety.InspectAsync(missing)).State == DatabaseSchemaState.Missing, "Missing detected without creation");
await Reject(() => DatabaseMigrationSafety.RequireCurrentAsync(missing), "Missing startup fails closed");
await Reject(() => DatabaseMaintenance.ApplyAsync(missing, Backups("missing"), true), "Implicit create refused");
Check(!File.Exists(missing), "Inspection/refusal did not create DB");
await Reject(() => DatabaseMaintenance.ApplyAsync(PathFor("offline"), Backups("offline"), false, true), "Offline acknowledgement required");
Check(!File.Exists(PathFor("offline")), "Refusal before creating a database");
var empty = PathFor("empty"); await File.WriteAllBytesAsync(empty, []);
Check((await DatabaseMigrationSafety.InspectAsync(empty)).State == DatabaseSchemaState.Empty, "Empty file detected");
await Reject(() => DatabaseMaintenance.ApplyAsync(empty, Backups("empty"), true), "Empty initialization requires explicit create");
var initial = await DatabaseMaintenance.ApplyAsync(empty, Backups("empty"), true, true);
Check(initial.BackupPath is not null && File.Exists(initial.BackupPath), "Empty existing file backed up before initializing");
Check((await DatabaseMigrationSafety.InspectAsync(empty)).State == DatabaseSchemaState.Current, "Explicit initialization works");
await DatabaseMigrationSafety.RequireCurrentAsync(empty); checks++;
var newFile = PathFor("fresh");
await DatabaseMaintenance.ApplyAsync(newFile, Backups("fresh"), true, true);
Check((await DatabaseMigrationSafety.InspectAsync(newFile)).State == DatabaseSchemaState.Current, "Missing initialization explicit and controlled");

var known = DatabaseMigrationSafety.KnownMigrations();
// All known previous versions must be recognized, not merely the most recent previous schema.
foreach (var version in known.Take(known.Length - 1)) {
    var file = PathFor(version);
    await using (var db = Context(file)) await db.GetService<IMigrator>().MigrateAsync(version);
    Check((await DatabaseMigrationSafety.InspectAsync(file)).State == DatabaseSchemaState.Pending, "Previous schema recognized: " + version);
    var before = await Stamp(file);
    await DatabaseMaintenance.ApplyAsync(file, Backups(version), true);
    DatabaseMigrationSafety.RequireSameData(before, await Stamp(file, before)); checks++;
    Check((await DatabaseMigrationSafety.InspectAsync(file)).State == DatabaseSchemaState.Current, "Previous version safely upgraded");
}

var existing = PathFor("representative");
var a = Guid.NewGuid(); var b = Guid.NewGuid(); var sid = Guid.NewGuid(); var oldToken = Guid.NewGuid();
const string password = "Isolated migration password 123";
await using (var db = Context(existing)) {
    await db.GetService<IMigrator>().MigrateAsync(known[^2]);
    var hasher = new PasswordHasher<User>();
    foreach (var id in new[] { a, b }) {
        var user = new User { Id = id, Username = id == a ? "fixture-a" : "fixture-b", Email = $"{id:N}@example.test", NormalizedEmail = $"{id:N}@EXAMPLE.TEST".ToUpperInvariant(),
            EmailVerified = id == a, FirstName = "Fixture", LastName = "Migration", HasCompletedOnboarding = true, AiSettingsJson = "{\"AiCustomInstructions\":\"Preserve\"}" };
        user.PasswordHash = hasher.HashPassword(user, password);
        var business = new Business { User = user, LegacyImported = true }; business.Profile.Name = "Preserved " + user.Username;
        business.Profile.Phone = ""; business.Profile.PrimaryColor = "#123456"; business.Profile.LogoDataUrl = "data:image/png;base64,ZmFrZQ==";
        db.Businesses.Add(business);
        db.Products.Add(new() { Business = business, Name = "Producto " + user.Username, Quantity = id == a ? 7 : 19, Cost = 2000.50m, DesiredProfitPercent = 35.25m, SalePrice = 2700.75m, ManualSalePrice = true });
        db.CalendarEntries.Add(new() { Business = business, Date = new(2026, 10, 8), Title = "Migración", Content = "Contenido conservado", Time = "12:34" });
        db.StockIntakeReceipts.Add(new() { BusinessId = business.Id, OperationKey = "preserved-receipt", ResultsJson = "[]" });
    }
    var legacy = new User { Username = "legacy-no-email" }; db.Businesses.Add(new Business { User = legacy, LegacyImported = true });
    db.AccountLoginSessions.Add(new() { Id = sid, UserId = a, ExpiresAt = DateTime.UtcNow.AddHours(8) });
    await db.SaveChangesAsync();
    var expiry = DateTime.UtcNow.AddHours(8);
    // The previous schema has no CreatedAt; retain the original legacy token fields exactly.
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AccountTokens (Id, UserId, Purpose, ExpiresAt) VALUES ({oldToken}, {a}, {"reset"}, {expiry})");
}
var baseline = await Stamp(existing);
await Reject(() => DatabaseMigrationSafety.RequireCurrentAsync(existing), "Pending version cannot start");
DatabaseMigrationSafety.RequireSameData(baseline, await Stamp(existing)); checks++;

// Keep a writer open so committed records stay in WAL during online backup.
await using var live = DatabaseMigrationSafety.Openable(existing, SqliteOpenMode.ReadWrite); await live.OpenAsync();
await using (var command = live.CreateCommand()) { command.CommandText = "PRAGMA wal_autocheckpoint=0"; await command.ExecuteNonQueryAsync(); }
await using (var command = live.CreateCommand()) { command.CommandText = "UPDATE Products SET Quantity=Quantity+1"; await command.ExecuteNonQueryAsync(); }
Check(File.Exists(existing + "-wal") && new FileInfo(existing + "-wal").Length > 0, "Committed representative data resides in live WAL");
baseline = await Stamp(existing);
var walBackup = await DatabaseMaintenance.BackupAsync(existing, PathFor("wal-backup"));
DatabaseMigrationSafety.RequireSameData(baseline, walBackup.Data); checks++;
var restore = await DatabaseMaintenance.RestoreToNewFileAsync(walBackup.Path, PathFor("restored-before"));
DatabaseMigrationSafety.RequireSameData(baseline, restore.Data); checks++;
Check((await DatabaseMigrationSafety.InspectAsync(restore.Path)).State == DatabaseSchemaState.Pending, "Restored backup retains previous schema");
await Reject(() => DatabaseMaintenance.RestoreToNewFileAsync(walBackup.Path, existing), "Restore refuses existing original");
DatabaseMigrationSafety.RequireSameData(baseline, await Stamp(existing)); checks++;
await live.CloseAsync();

var changed = await DatabaseMaintenance.ApplyAsync(existing, Backups("representative"), true);
Check(changed.Status == "Completed" && changed.BackupPath is not null, "Verified backup before upgrading representative DB");
DatabaseMigrationSafety.RequireSameData(baseline, await Stamp(existing, baseline)); checks++;
await using (var db = Context(existing)) {
    Check(await db.Users.CountAsync() == 3 && await db.Businesses.CountAsync() == 3 && await db.Products.CountAsync() == 2, "Accounts and businesses not duplicated");
    Check((await db.AccountTokens.SingleAsync()).CreatedAt == DateTime.MinValue, "Old token default does not infer a send timestamp");
    Check((await db.Users.SingleAsync(u => u.Id == a)).EmailVerified && !(await db.Users.SingleAsync(u => u.Id == b)).EmailVerified, "Verification flags unchanged");
    var legacy = await db.Users.SingleAsync(u => u.Username == "legacy-no-email");
    Check(legacy.Email is null && legacy.PasswordHash is null && !legacy.EmailVerified, "Old accounts not silently linked or verified");
    Check(!await db.AccountEmailNotifications.AnyAsync(), "Migration does not send/queue mail or seed accounts");
    db.AccountEmailNotifications.Add(new() { UserId = Guid.NewGuid() });
    try { await db.SaveChangesAsync(); throw new Exception("Notification orphan accepted"); } catch (DbUpdateException) { checks++; }
    var owner = await db.Businesses.Where(x => x.UserId == a).Select(x => x.Id).SingleAsync();
    Check((await db.Products.Where(p => p.BusinessId == owner).SingleAsync()).Quantity == 8, "A ownership and stock preserved");
}
var factory = new Factory(new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite($"Data Source={existing};Pooling=False").Options);
var accounts = new AccountService(factory, new EphemeralDataProtectionProvider(), new UnavailableAccountEmail());
var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, a.ToString()), new Claim("sid", sid.ToString())], "fixture"));
Check(await accounts.IsSessionValidAsync(principal), "Existing verified user's session remains valid");
var address = $"{a:N}@example.test";
Check(await accounts.AuthenticateAsync(address, password) is not null, "Preserved password hash still authenticates");
var current = await Stamp(existing);
var schemaBefore = (await DatabaseMigrationSafety.InspectAsync(existing)).Applied;
var again = await DatabaseMaintenance.ApplyAsync(existing, Backups("representative"), true);
Check(again.Status == "AlreadyCurrent" && again.BackupPath is null, "Idempotent re-execution performs no schema update");
DatabaseMigrationSafety.RequireSameData(current, await Stamp(existing)); checks++;
var reapplied = (await DatabaseMigrationSafety.InspectAsync(existing)).Applied;
Check(schemaBefore.SequenceEqual(reapplied), "History not duplicated");
using (DatabaseMigrationSafety.ApplicationLease(existing)) {
    await Reject(() => DatabaseMaintenance.ApplyAsync(existing, Backups("locked"), true), "Running app lease blocks maintenance");
    using var secondApp = DatabaseMigrationSafety.ApplicationLease(existing); checks++;
}
using (DatabaseMigrationSafety.MaintenanceLease(existing)) {
    await Reject(() => Task.FromResult(DatabaseMigrationSafety.ApplicationLease(existing)), "Maintenance blocks application startup");
}
using (DatabaseMigrationSafety.ApplicationLease(existing, singleInstance: true)) {
    await Reject(() => Task.FromResult(DatabaseMigrationSafety.ApplicationLease(existing, singleInstance: true)), "Production rejects a second instance");
    await Reject(() => Task.FromResult(DatabaseMigrationSafety.ApplicationLease(existing)), "Production excludes shared application leases");
    await Reject(() => DatabaseMaintenance.ApplyAsync(existing, Backups("production-locked"), true), "Production excludes maintenance");
}
using (DatabaseMigrationSafety.ApplicationLease(existing, singleInstance: true)) { checks++; }

var failing = (await DatabaseMaintenance.RestoreToNewFileAsync(walBackup.Path, PathFor("failing"))).Path;
var injected = new MigrationFailure();
await Reject(() => DatabaseMaintenance.ApplyAsync(failing, Backups("failure"), true, testInterceptors: [injected]), "Failure after first ALTER before notification table");
Check(injected.AlterObserved && injected.FailureObserved, "Failure actually occurred after executing ALTER, not before migration");
Check((await DatabaseMigrationSafety.InspectAsync(failing)).State == DatabaseSchemaState.Pending, "Failure leaves a recognized old schema; startup stays blocked");
DatabaseMigrationSafety.RequireSameData(baseline, await Stamp(failing)); checks++;
var failureReceipt = Directory.GetFiles(Backups("failure"), "*.migration-result.json").Single();
using (var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(failureReceipt))) Check(receipt.RootElement.GetProperty("Status").GetString() == "Failed", "Failure receipt persisted without SQL/row values");
await DatabaseMaintenance.ApplyAsync(failing, Backups("retry"), true); checks++;
var returned = await DatabaseMaintenance.RestoreToNewFileAsync(Directory.GetFiles(Backups("failure"), "*.db").Single(), PathFor("restored-failure"));
DatabaseMigrationSafety.RequireSameData(baseline, returned.Data); checks++;

// Drift, manual/unknown history, broken FKs and stale migration locks must never be guessed/repaired.
var drift = (await DatabaseMaintenance.RestoreToNewFileAsync(walBackup.Path, PathFor("drift"))).Path;
await using (var db = Context(drift)) await db.Database.ExecuteSqlRawAsync("DROP INDEX IX_Users_NormalizedEmail");
Check((await DatabaseMigrationSafety.InspectAsync(drift)).State == DatabaseSchemaState.Incompatible, "Physical index drift detected despite valid history");
await Reject(() => DatabaseMaintenance.ApplyAsync(drift, Backups("drift"), true), "Drift refused before write");
var unknown = PathFor("unknown");
await using (var db = Context(unknown)) await db.Database.ExecuteSqlRawAsync("CREATE TABLE ManualTable (Id INTEGER PRIMARY KEY)");
Check((await DatabaseMigrationSafety.InspectAsync(unknown)).State == DatabaseSchemaState.Incompatible, "Manual schema without history refused");
var future = (await DatabaseMaintenance.RestoreToNewFileAsync(walBackup.Path, PathFor("future"))).Path;
await using (var db = Context(future)) await db.Database.ExecuteSqlRawAsync("INSERT INTO __EFMigrationsHistory VALUES ('20990101000000_Unknown', '10.0.12')");
Check((await DatabaseMigrationSafety.InspectAsync(future)).State == DatabaseSchemaState.Incompatible, "Future history refused without downgrade");
var locked = (await DatabaseMaintenance.RestoreToNewFileAsync(walBackup.Path, PathFor("stale-lock"))).Path;
await using (var db = Context(locked)) await db.Database.ExecuteSqlRawAsync("INSERT INTO __EFMigrationsLock (Id, Timestamp) VALUES (1, '2026-10-08')");
Check((await DatabaseMigrationSafety.InspectAsync(locked)).State == DatabaseSchemaState.Locked, "Stale EF lock detected");
await Reject(() => DatabaseMaintenance.ApplyAsync(locked, Backups("stale"), true), "Stale lock not deleted automatically");
var orphans = (await DatabaseMaintenance.RestoreToNewFileAsync(walBackup.Path, PathFor("orphan"))).Path;
await using (var connection = DatabaseMigrationSafety.Openable(orphans, SqliteOpenMode.ReadWrite)) {
    await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA foreign_keys=OFF; UPDATE Products SET BusinessId='00000000-0000-0000-0000-000000000000'"; await command.ExecuteNonQueryAsync();
}
Check((await DatabaseMigrationSafety.InspectAsync(orphans)).State == DatabaseSchemaState.Incompatible, "Foreign key damage detected");
await Reject(() => DatabaseMaintenance.BackupAsync(existing, Path.Combine(root, "wwwroot", "unsafe.db")), "Public backup rejected");

if (args.Length == 1) {
    var publish = Path.GetFullPath(args[0]);
    Check(File.Exists(Path.Combine(publish, "Aquarella.dll")) && !Directory.Exists(Path.Combine(publish, "App_Data")), "Isolated publish only");
    await RefusedStartup(walBackup.Path, "Pending startup never migrates");
    await RefusedStartup(drift, "Drift startup never repairs");
    await RefusedStartup(PathFor("app-missing"), "Missing startup never initializes");
    await HealthyStartup(); await HealthyStartup();

    ProcessStartInfo Start(string database, int port) {
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = publish, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(Path.Combine(publish, "Aquarella.dll"));
        info.Environment["DOTNET_ENVIRONMENT"] = info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        info.Environment["PORT"] = port.ToString(); info.Environment["ProductionStorage__Root"] = root;
        info.Environment["DataProtection__KeysPath"] = Path.Combine(root, "keys"); info.Environment["ConnectionStrings__Aquarella"] = $"Data Source={database}";
        info.Environment["Accounts__PublicOrigin"] = "https://aquarella.example.test"; info.Environment["AllowedHosts"] = "aquarella.example.test";
        info.Environment["ReverseProxy__KnownProxies__0"] = "127.0.0.1"; info.Environment["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "false";
        info.Environment["Email__Smtp__Enabled"] = "false"; info.Environment["Logging__EventLog__LogLevel__Default"] = "None";
        return info;
    }
    async Task RefusedStartup(string file, string name) {
        var existed = File.Exists(file); var before = existed ? await Stamp(file) : null;
        using var server = Process.Start(Start(file, FreePort()))!;
        var stdout = server.StandardOutput.ReadToEndAsync(); var stderr = server.StandardError.ReadToEndAsync();
        try { await server.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { if (!server.HasExited) { server.Kill(true); await server.WaitForExitAsync(); } }
        var messages = await stdout + await stderr;
        Check(server.ExitCode != 0 && messages.Contains("base", StringComparison.OrdinalIgnoreCase), name);
        if (before is not null) { DatabaseMigrationSafety.RequireSameData(before, await Stamp(file)); checks++; }
        else Check(!File.Exists(file), "Refused process created no database");
    }
    async Task HealthyStartup() {
        var port = FreePort(); using var server = Process.Start(Start(existing, port))!;
        var stdout = server.StandardOutput.ReadToEndAsync(); var stderr = server.StandardError.ReadToEndAsync();
        try {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(2) };
            var ready = false;
            for (var i = 0; i < 70; i++) {
                if (server.HasExited) throw new Exception("Upgraded app failed: " + await stderr);
                try { using var request = new HttpRequestMessage(HttpMethod.Get, "/login"); request.Headers.Host = "aquarella.example.test"; request.Headers.Add("X-Forwarded-Proto", "https"); request.Headers.Add("X-Real-IP", "198.51.100.9");
                    using var response = await client.SendAsync(request); if (response.StatusCode == HttpStatusCode.OK) { ready = true; break; }
                } catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { }
                await Task.Delay(100);
            }
            Check(ready, "Upgraded/restarted Production app serves Login");
            await Reject(() => DatabaseMaintenance.ApplyAsync(existing, Backups("live-server"), true), "Actual running process blocks migration");
        }
        finally { if (!server.HasExited) { server.Kill(true); await server.WaitForExitAsync(); } }
        var messages = await stdout + await stderr;
        Check(!messages.Contains("fail:") && !messages.Contains("Unhandled exception"), "Compatible startup has no server error");
        DatabaseMigrationSafety.RequireSameData(current, await Stamp(existing)); checks++;
    }
}
Console.WriteLine($"PASS migration safety: {checks} checks; empty/all earlier versions, A/B representative data, WAL backup/restore, hashes/sessions, rollback/retry, drift/history/FK/locks and idempotence.");
Console.WriteLine(args.Length == 1 ? "PASS isolated application startup refusal and two successful starts after upgrade." : "Application process checks skipped: pass an isolated publish directory to run them.");
Console.WriteLine("Temporary artifacts: " + root);

static int FreePort() { using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
sealed class Factory(DbContextOptions<AquarellaDbContext> options) : IDbContextFactory<AquarellaDbContext> { public AquarellaDbContext CreateDbContext() => new(options); }
sealed class MigrationFailure : DbCommandInterceptor {
    public bool AlterObserved { get; private set; }
    public bool FailureObserved { get; private set; }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) {
        if (command.CommandText.StartsWith("ALTER TABLE \"AccountTokens\"", StringComparison.Ordinal)) AlterObserved = true;
        if (command.CommandText.StartsWith("CREATE TABLE \"AccountEmailNotifications\"", StringComparison.Ordinal)) { FailureObserved = true; throw new InvalidOperationException("Controlled migration failure after ALTER"); }
        return ValueTask.FromResult(result);
    }
}
