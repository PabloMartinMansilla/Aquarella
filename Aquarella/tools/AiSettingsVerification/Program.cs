using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Aquarella.Data;
using Aquarella.Models;
using Aquarella.Services;
using Aquarella.Services.Accounts;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

var path = Path.Combine(Path.GetTempPath(), $"aquarella-ai-settings-{Guid.NewGuid():N}.db");
var factory = new Factory(new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite($"Data Source={path}").Options);
var first = Guid.NewGuid(); var second = Guid.NewGuid(); var business = Guid.NewGuid();
var checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
await using (var db = factory.CreateDbContext())
{
    Check(!db.Database.HasPendingModelChanges(), "Migration snapshot matches model");
    var assembly = db.GetService<IMigrationsAssembly>();
    var migration = assembly.CreateMigration(assembly.Migrations.Values.Last(), db.Database.ProviderName!);
    Check(migration.UpOperations.Count == 1 && migration.UpOperations[0] is AddColumnOperation { Name: "AiSettingsJson", Table: "Users", IsNullable: true }, "Exactly one nullable column added");
    await db.GetService<IMigrator>().MigrateAsync("20261004211838_RealAccounts");
    var now = DateTime.UtcNow;
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Users (Id, Username, FirstName, LastName, EmailVerified, HasCompletedOnboarding, CreatedAt, UpdatedAt) VALUES ({first}, {"first"}, {"Ana"}, {"Prueba"}, 0, 0, {now}, {now})");
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Users (Id, Username, FirstName, LastName, EmailVerified, HasCompletedOnboarding, CreatedAt, UpdatedAt) VALUES ({second}, {"second"}, {"Luis"}, {"Prueba"}, 0, 0, {now}, {now})");
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Businesses (Id, UserId, Profile_Name, Profile_PrimaryColor, Profile_SecondaryColor, Profile_TertiaryColor, LegacyImported, CreatedAt, UpdatedAt) VALUES ({business}, {first}, {"Negocio conservado"}, {"#F7F9F6"}, {"#28745B"}, {"#203E32"}, 1, {now}, {now})");
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Products (Id, BusinessId, Name, Quantity, Cost, DesiredProfitPercent, SalePrice, ManualSalePrice, CreatedAt, UpdatedAt) VALUES ({Guid.NewGuid()}, {business}, {"Producto conservado"}, 7, {120.5m}, 25, 150, 1, {now}, {now})");
    await db.Database.MigrateAsync();
    Check((await db.Users.SingleAsync(u => u.Id == first)).AiSettingsJson is null, "Existing users retain null settings");
    Check((await db.Businesses.SingleAsync()).Profile.Name == "Negocio conservado" && (await db.Products.SingleAsync()).Quantity == 7, "Business and stock survive upgrade");
}
var accounts = new AccountService(factory, new EphemeralDataProtectionProvider(), new NoEmail());
var firstSession = await Session(first); var secondSession = await Session(second);
var firstStore = new AiSettingsStore(factory, firstSession.Session); var secondStore = new AiSettingsStore(factory, secondSession.Session);
var defaults = await firstStore.LoadAsync();
Check(defaults.AiResponseDetail == AiResponseDetail.Balanced && defaults.AiResponseTone == AiResponseTone.Friendly, "Response defaults");
Check(!defaults.AiCanAccessStock && !defaults.AiCanAccessPricing && !defaults.AiCanAccessAgenda && !defaults.AiCanAccessAlerts, "No data permissions by default");
Check(!defaults.AiProactiveSuggestionsEnabled && defaults.AiProactivityLevel == AiProactivityLevel.Low && defaults.AiExplainRecommendations, "Conservative behavior defaults");
defaults.AiResponseDetail = AiResponseDetail.Detailed; defaults.AiResponseTone = AiResponseTone.Professional;
defaults.AiCustomInstructions = "Priorizá reducir desperdicios. <script>texto</script>";
defaults.AiCanAccessStock = true; defaults.AiCanAccessPricing = true; defaults.AiCanAccessAgenda = true; defaults.AiCanAccessAlerts = true;
defaults.AiProactiveSuggestionsEnabled = true; defaults.AiProactivityLevel = AiProactivityLevel.High; defaults.AiExplainRecommendations = false;
await firstStore.SaveAsync(defaults);
var reload = await new AiSettingsStore(factory, (await Session(first)).Session).LoadAsync();
Check(AiSettingsCodec.Write(reload) == AiSettingsCodec.Write(defaults), "All settings persist after new login/session");
Check(!((await secondStore.LoadAsync()).AiCanAccessStock), "Second user retains separate defaults");
await secondStore.SaveAsync(new AiSettings { AiResponseDetail = AiResponseDetail.Brief });
Check((await firstStore.LoadAsync()).AiResponseDetail == AiResponseDetail.Detailed && (await secondStore.LoadAsync()).AiResponseDetail == AiResponseDetail.Brief, "Writes isolated between users");
await using (var db = factory.CreateDbContext())
{
    Check((await db.Businesses.SingleAsync()).Profile.PrimaryColor == "#F7F9F6" && (await db.Products.SingleAsync()).Cost == 120.5m, "Settings save preserves business and prices");
    var json = (await db.Users.SingleAsync(u => u.Id == first)).AiSettingsJson!;
    Check(!json.Contains("Memory") && !json.Contains("History") && !json.Contains("WebSearch") && !json.Contains("ActionMode"), "No inert capabilities persisted");
}
Check(AiSettingsCodec.Read("{}").AiExplainRecommendations, "Missing JSON properties use defaults");
try { await firstStore.SaveAsync(new() { AiCustomInstructions = new string('x', 2001) }); throw new Exception("Long instructions accepted"); } catch (ValidationException) { checks++; }
try { await firstStore.SaveAsync(new() { AiResponseTone = (AiResponseTone)99 }); throw new Exception("Invalid enum accepted"); } catch (ValidationException) { checks++; }
await firstStore.SaveAsync(new() { AiCustomInstructions = "" });
Check((await firstStore.LoadAsync()).AiCustomInstructions == "", "Optional blank instructions save");
await using (var db = factory.CreateDbContext()) await db.Users.Where(u => u.Id == first).ExecuteUpdateAsync(s => s.SetProperty(u => u.AiSettingsJson, "broken json"));
try { await firstStore.LoadAsync(); throw new Exception("Invalid stored JSON silently replaced"); } catch (JsonException) { checks++; }
await using (var db = factory.CreateDbContext())
{
    Check((await db.Users.SingleAsync(u => u.Id == first)).AiSettingsJson == "broken json", "Bad data remains untouched on load");
    await db.AccountLoginSessions.Where(s => s.Id == firstSession.Id).ExecuteDeleteAsync();
}
try { await firstStore.SaveAsync(new()); throw new Exception("Revoked session write accepted"); } catch (InvalidOperationException) { checks++; }
try { await firstStore.LoadAsync(); throw new Exception("Revoked session read accepted"); } catch (InvalidOperationException) { checks++; }
Console.WriteLine($"{checks} comprobaciones correctas: migración aditiva, conservación de datos, defaults, persistencia, aislamiento, validación y sesiones revocadas.");
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
File.Delete(path);
async Task<(AccountSession Session, Guid Id)> Session(Guid user)
{
    await using var db = factory.CreateDbContext();
    var login = new AccountLoginSession { UserId = user, ExpiresAt = DateTime.UtcNow.AddHours(1) }; db.AccountLoginSessions.Add(login); await db.SaveChangesAsync();
    var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString()), new Claim("sid", login.Id.ToString())], "test"));
    return (new AccountSession(new TestAuthentication(principal), accounts, factory), login.Id);
}
sealed class Factory(DbContextOptions<AquarellaDbContext> options) : IDbContextFactory<AquarellaDbContext> { public AquarellaDbContext CreateDbContext() => new(options); }
sealed class TestAuthentication(ClaimsPrincipal principal) : AuthenticationStateProvider { public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal)); }
sealed class NoEmail : IAccountEmail { public bool Available => false; public Task SendAsync(string recipient, string purpose, string link) => throw new Exception("Unexpected external operation"); }
