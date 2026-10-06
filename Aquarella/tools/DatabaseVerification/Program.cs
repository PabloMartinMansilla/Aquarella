using Aquarella.Data;
using Aquarella.Models;
using Aquarella.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Aquarella.Services.Accounts;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Claims;

var transientProfile = new TransientProfilePersistence();
var profileStore = new BusinessProfileStore(transientProfile);
try { await profileStore.InitializeAsync(); throw new Exception("Expected transient profile load failure"); }
catch (IOException) { }
Check(profileStore.Load().Name == "Aquarella", "failed profile load preserves default identity");
await profileStore.InitializeAsync();
Check(profileStore.Load().Name == "Perfil recuperado" && transientProfile.Loads == 2, "profile initialization retries after transient failure");
await profileStore.InitializeAsync();
Check(transientProfile.Loads == 2, "successful profile initialization remains cached");
Console.WriteLine("PASS: profile initialization recovers from transient failure without resetting valid data.");

var path = Path.Combine(Path.GetTempPath(), $"aquarella-verification-{Guid.NewGuid():N}.db");
var options = new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite($"Data Source={path}").Options;
var factory = new Factory(options);
await using (var db = factory.CreateDbContext()) {
    await db.Database.MigrateAsync();
    var user = new User { Username = "pablo" };
    db.Users.Add(user); db.Businesses.Add(new Business { User = user });
    var other = new User { Username = "other" };
    db.Users.Add(other); db.Businesses.Add(new Business { User = other, LegacyImported = true });
    await db.SaveChangesAsync();
}
var productId = Guid.NewGuid(); var noteId = Guid.NewGuid();
var js = new FakeJs(new BusinessData.LegacyData {
    Profile = new BusinessProfile { Name = "Negocio importado", LogoDataUrl = "data:image/png;base64,aGVsbG8=" },
    Products = [new StockProduct(productId, "Producto importado", 4)],
    Prices = [new ProductPrice { ProductId = productId, Cost = 12.35m, DesiredProfitPercent = 30, SalePrice = 19.75m, ManualSalePrice = true }],
    Notes = [new AgendaNote(noteId, new DateOnly(2026, 10, 4), "", "Sólo comentario", null)]
});
var accountService = new AccountService(factory, new EphemeralDataProtectionProvider(), new NullEmail());
Guid userId, sessionId;
await using (var db = factory.CreateDbContext()) {
    userId = await db.Users.Where(u => u.Username == "pablo").Select(u => u.Id).SingleAsync();
    var login = new AccountLoginSession { UserId = userId, ExpiresAt = DateTime.UtcNow.AddHours(1) };
    sessionId = login.Id; db.AccountLoginSessions.Add(login); await db.SaveChangesAsync();
}
var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim("sid", sessionId.ToString()) }, "test"));
var session = new AccountSession(new TestAuthentication(principal), accountService, factory);
await session.InitializeAsync();
var data = new BusinessData(factory, session, js);
await data.EnsureImportedAsync();
Check((await data.LoadProfileAsync()).Name == "Negocio importado", "legacy profile import");
Check((await data.LoadProductsAsync()).Single().Id == productId, "preserve product ID");
Check((await data.LoadPricesAsync()).Single().Cost == 12.35m, "exact decimals and prices import");
Check((await data.LoadNotesAsync()).Single().Content == "Sólo comentario", "optional note import");
js.Legacy.Profile!.Name = "Must not replace saved profile";
var second = new BusinessData(factory, session, js); await second.EnsureImportedAsync();
Check((await second.LoadProfileAsync()).Name == "Negocio importado", "import idempotence");
await data.AdjustAsync(productId, 3);
Check((await data.LoadProductsAsync()).Single().Quantity == 7, "stock adjustment");
try { await data.AdjustAsync(productId, -8); throw new Exception("negative stock accepted"); } catch (ArgumentException) { }
await data.SaveProductAsync(new StockProduct(productId, "Renamed", 7), false);
Check((await data.LoadPricesAsync()).Single().SalePrice == 19.75m, "rename preserves prices");
var price = (await data.LoadPricesAsync()).Single(); price.Cost = 20.01m; price.DesiredProfitPercent = 25; price.SalePrice = 30.10m;
await data.SavePriceAsync(price);
Check((await data.LoadPricesAsync()).Single().RealProfit == 10.09m, "real profit decimal");
await data.SaveNoteAsync(new AgendaNote(noteId, new DateOnly(2026, 11, 4), "Title only", "", "15:30"));
Check((await data.LoadNotesAsync()).Single().Date.Month == 11, "edit note date and optional content");
await using (var db = factory.CreateDbContext()) {
    var otherId = await db.Businesses.Where(b => b.User.Username == "other").Select(b => b.Id).SingleAsync();
    var foreign = new Product { BusinessId = otherId, Name = "Foreign", Quantity = 1 }; db.Products.Add(foreign); await db.SaveChangesAsync();
    await data.DeleteProductAsync(foreign.Id);
    Check(await db.Products.AnyAsync(p => p.Id == foreign.Id), "cross business delete blocked");
}
Check((await new BusinessData(factory, session, js).LoadProductsAsync()).Count == 1, "business scoped reads after new service");
await data.DeleteNoteAsync(noteId); await data.DeleteProductAsync(productId);
Check((await data.LoadNotesAsync()).Count == 0 && (await data.LoadPricesAsync()).Count == 0, "delete note and product/prices");
var registration = await accountService.RegisterAsync("Ana", "Prueba", "ana@example.test", "Una frase segura 123");
Check(registration.User is not null, "register valid account");
var registered = registration.User!;
Check(registered.PasswordHash is not null && registered.PasswordHash != "Una frase segura 123", "hashed password only");
Check((await accountService.RegisterAsync("Ana", "Prueba", "ANA@example.test", "Otra frase segura")).User is null, "normalized email uniqueness");
Check((await accountService.RegisterAsync("Ana", "Prueba", "invalid", "Otra frase segura")).User is null, "invalid email");
Check((await accountService.RegisterAsync("Ana", "Prueba", "next@example.test", "short")).User is null, "short password");
Check(await accountService.AuthenticateAsync("ana@example.test", "Una frase segura 123") is null, "unverified account denied");
var verification = await accountService.IssueTokenAsync(registered.Id, "verify");
Check(await accountService.ConsumeAsync(verification, "verify"), "verify valid token");
Check(!await accountService.ConsumeAsync(verification, "verify"), "verification single use");
Check(await accountService.AuthenticateAsync("ana@example.test", "Una frase segura 123") is not null, "valid login");
Check(await accountService.AuthenticateAsync("ana@example.test", "una frase segura 123") is null, "password case sensitive");
Check(await accountService.AuthenticateAsync("missing@example.test", "Una frase segura 123") is null, "unknown email login");
var reset = await accountService.IssueTokenAsync(registered.Id, "reset");
var newerReset = await accountService.IssueTokenAsync(registered.Id, "reset");
Check(!await accountService.TokenValidAsync(reset, "reset"), "resend invalidates old token");
Check(!await accountService.ConsumeAsync(newerReset, "verify"), "token purpose isolation");
Check(await accountService.ConsumeAsync(newerReset, "reset", "Nueva frase segura 456"), "reset password");
Check(!await accountService.ConsumeAsync(newerReset, "reset", "Otra frase segura 789"), "reset single use");
Check(await accountService.AuthenticateAsync("ana@example.test", "Una frase segura 123") is null, "old password denied");
Check(await accountService.AuthenticateAsync("ana@example.test", "Nueva frase segura 456") is not null, "new password accepted");
try { await new DevelopmentAccountEmail(new EnvironmentStub { EnvironmentName = "Production" }).SendAsync("ana@example.test", "verify", "https://example.test/private"); throw new Exception("development email enabled in Production"); } catch (InvalidOperationException) { }
Guid accountLoginId;
await using (var db = factory.CreateDbContext()) {
    var row = new AccountLoginSession { UserId = registered.Id, ExpiresAt = DateTime.UtcNow.AddHours(1) }; accountLoginId = row.Id; db.AccountLoginSessions.Add(row); await db.SaveChangesAsync();
}
var newPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, registered.Id.ToString()), new Claim("sid", accountLoginId.ToString()) }, "test"));
var newSession = new AccountSession(new TestAuthentication(newPrincipal), accountService, factory);
Check((await new BusinessData(factory, newSession, js).LoadProductsAsync()).Count == 0, "new accounts never read another user's legacy inventory");
var expired = await accountService.IssueTokenAsync(registered.Id, "reset", TimeSpan.FromSeconds(-1));
Check(!await accountService.TokenValidAsync(expired, "reset"), "expired token rejected");
Check(!await accountService.TokenValidAsync("tampered-token", "reset"), "tampered token rejected");
Check(!await session.CompletedTutorialAsync(), "new user's tutorial pending");
await session.CompleteTutorialAsync();
Check(await new AccountSession(new TestAuthentication(principal), accountService, factory).CompletedTutorialAsync(), "tutorial persisted by user");
session.RequestTutorial(); Check(session.ConsumeTutorialRequest() && !session.ConsumeTutorialRequest(), "manual tutorial replay");
await using (var db = factory.CreateDbContext()) {
    Check(!(await db.Users.SingleAsync(u => u.Id == registered.Id)).HasCompletedOnboarding, "tutorial isolated between users");
    await db.AccountLoginSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
}
try { await data.LoadProductsAsync(); throw new Exception("revoked session accepted"); } catch (InvalidOperationException) { }
Console.WriteLine("PASS: account registration, hashes, duplicate/invalid emails, password validation/case, verification/reset expiry/single use/purpose/resend, tutorial per-user, revoked sessions.");
var legacyLink = await accountService.IssueTokenAsync(userId, "legacy");
Guid preservedBusiness;
await using (var db = factory.CreateDbContext()) preservedBusiness = await db.Businesses.Where(b => b.UserId == userId).Select(b => b.Id).SingleAsync();
var linked = await accountService.RegisterAsync("Pablo", "Desarrollo", "legacy@example.test", "Una frase de vinculación", legacyLink);
Check(linked.User?.Id == userId, "legacy linking preserves user ID");
await using (var db = factory.CreateDbContext()) {
    var business = await db.Businesses.SingleAsync(b => b.UserId == userId);
    Check(business.Id == preservedBusiness && business.Profile.Name == "Negocio importado", "legacy linking preserves business/profile");
}
Check((await accountService.RegisterAsync("Pablo", "Desarrollo", "legacy2@example.test", "Una frase de vinculación", legacyLink)).User is null, "legacy linking single use");
Console.WriteLine("PASS: controlled legacy linking retains user/business IDs and profile.");
var existingPath = Path.GetFullPath("Aquarella/Aquarella/App_Data/aquarella.db");
var backupPath = Path.GetFullPath("Aquarella/Aquarella/App_Data/aquarella.before-accounts.db");
if (File.Exists(existingPath) && File.Exists(backupPath)) {
    foreach (var table in new[] { "Businesses", "Products", "CalendarEntries" }) Check(await Snapshot(existingPath, table) == await Snapshot(backupPath, table), "actual data unchanged after migration: " + table);
    Console.WriteLine("PASS: actual business/profile/products/prices/agenda match pre-migration backup.");
}
Console.WriteLine("PASS: migrations, import, idempotence, profile/logo, stock, decimals, prices, notes, business isolation, fresh contexts.");
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
File.Delete(path); if (File.Exists(path + "-wal")) File.Delete(path + "-wal"); if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static async Task<string> Snapshot(string file, string table) {
    using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Mode=ReadOnly"); await connection.OpenAsync();
    using var command = connection.CreateCommand(); command.CommandText = $"SELECT * FROM {table} ORDER BY Id";
    using var reader = await command.ExecuteReaderAsync(); var rows = new List<object[]>();
    while (await reader.ReadAsync()) { var values = new object[reader.FieldCount]; reader.GetValues(values); rows.Add(values); }
    return System.Text.Json.JsonSerializer.Serialize(rows);
}
sealed class Factory(DbContextOptions<AquarellaDbContext> options) : IDbContextFactory<AquarellaDbContext> { public AquarellaDbContext CreateDbContext() => new(options); }
sealed class FakeJs(BusinessData.LegacyData legacy) : IJSRuntime, IJSObjectReference {
    public BusinessData.LegacyData Legacy => legacy;
    public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => new(identifier switch { "import" => (T)(object)this, "load" => (T)(object)true, "read" => (T)(object)legacy, _ => default! });
    public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken token, object?[]? args) => InvokeAsync<T>(identifier, args);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
sealed class EnvironmentStub : IWebHostEnvironment {
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "Tests";
    public string ContentRootPath { get; set; } = "";
    public string WebRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}

sealed class TestAuthentication(ClaimsPrincipal principal) : AuthenticationStateProvider { public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal)); }
sealed class NullEmail : IAccountEmail { public bool Available => true; public Task SendAsync(string recipient, string purpose, string link) => Task.CompletedTask; }
sealed class TransientProfilePersistence : IBusinessProfilePersistence
{
    public int Loads { get; private set; }
    public Task<BusinessProfile?> LoadAsync() => ++Loads == 1
        ? Task.FromException<BusinessProfile?>(new IOException("Isolated transient fixture failure"))
        : Task.FromResult<BusinessProfile?>(new() { Name = "Perfil recuperado" });
    public Task<BusinessProfile> SaveAsync(BusinessProfile profile, BusinessProfile original) => Task.FromResult(profile.Copy());
}
