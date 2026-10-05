using System.Security.Claims;
using Aquarella.Data;
using Aquarella.Models;
using Aquarella.Services;
using Aquarella.Services.Accounts;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
NoticeProduct Item(string name, int quantity, decimal cost, decimal sale, bool manual = true, decimal desired = 0) =>
    new(new(Guid.NewGuid(), name, quantity), new() { Cost = cost, SalePrice = sale, ManualSalePrice = manual, DesiredProfitPercent = desired });
var data = new[] { Item("Normal", 10, 100, 150), Item("Pérdida", 2, 200, 190), Item("Sin precios", 3, 0, 0, false), Item("Sin stock", 0, 50, 50), Item("Sin costo", 1, 0, 20) };
var metrics = MetricsCalculator.Calculate(data);
Check(metrics.ProductCount == 5 && metrics.Units == 16 && metrics.OutOfStock == 1, "Product, unit and out-of-stock counts");
Check(metrics.InventoryCost == 1400 && metrics.InventorySaleValue == 1900, "Decimal inventory valuations");
Check(metrics.ProductsInStock == 4 && metrics.ProductsWithCost == 2 && metrics.ProductsWithSalePrice == 3, "Explicit partial coverage");
Check(metrics.WeightedProfitPercent == (1880m - 1400m) / 1400m * 100, "Cost-weighted percentage uses same known-cost stock on both sides");
Check(metrics.ProductsWithProfit == 3 && metrics.LowestProfit[0].Name == "Pérdida" && metrics.HighestProfit[0].Name == "Normal", "Profit rankings exclude unknown cost");
Check(metrics.ActiveNotices == 3 && metrics.ProductsSellingAtLoss == 1 && metrics.ProductsWithCriticalProfit == 1, "Notice causes reuse deterministic rules");
var missing = MetricsCalculator.Calculate([Item("Pending", 10, 0, 0, false)]);
Check(missing.InventoryCost is null && missing.InventorySaleValue is null && missing.WeightedProfitPercent is null, "Missing pricing is not presented as zero-valued stock");
var free = MetricsCalculator.Calculate([Item("Explicit free sale", 2, 100, 0)]);
Check(free.InventorySaleValue == 0 && free.WeightedProfitPercent == -100 && free.ProductsSellingAtLoss == 1, "Explicit manual zero price is a real price");
var automatic = MetricsCalculator.Calculate([Item("Suggested", 2, 12.35m, 999, false, 30)]);
Check(automatic.InventorySaleValue == 32.12m, "Existing suggested price rounding reused");
var zeroStock = MetricsCalculator.Calculate([Item("Zero units", 0, 0, 0, false)]);
Check(zeroStock.InventoryCost == 0 && zeroStock.InventorySaleValue == 0 && zeroStock.WeightedProfitPercent is null, "No stock has no inventory value, no invented percentage");
Check(MetricsCalculator.Calculate([]).ProductCount == 0, "Empty business");
Check(MetricsCalculator.Calculate([Item("One", int.MaxValue, 1, 2), Item("Two", int.MaxValue, 1, 2)]).Units == 4294967294L, "Unit total avoids integer overflow");
Check(MetricsCalculator.Calculate(Enumerable.Range(0, 40).Select(i => Item($"Product {i}", 1, 100, 100 + i)).ToArray()).LowestProfit.Count == 5, "Bounded rankings");
var path = Path.Combine(Path.GetTempPath(), $"aquarella-metrics-{Guid.NewGuid():N}.db");
var factory = new Factory(new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite($"Data Source={path}").Options);
var first = new User { Username = "metrics-first" }; var other = new User { Username = "metrics-other" };
var firstBusiness = new Business { User = first, LegacyImported = true }; var otherBusiness = new Business { User = other, LegacyImported = true };
await using (var db = factory.CreateDbContext())
{
    Check(!db.Database.HasPendingModelChanges(), "No schema change for metrics");
    await db.Database.MigrateAsync();
    db.Businesses.AddRange(firstBusiness, otherBusiness);
    foreach (var item in data) db.Products.Add(new() { Id = item.Product.Id, BusinessId = firstBusiness.Id, Name = item.Product.Name, Quantity = item.Product.Quantity, Cost = item.Price.Cost, SalePrice = item.Price.SalePrice, ManualSalePrice = item.Price.ManualSalePrice });
    await db.SaveChangesAsync();
}
var accounts = new AccountService(factory, new EphemeralDataProtectionProvider(), new NoEmail());
var session = await Session(first.Id); var otherSession = await Session(other.Id);
using var businessData = new BusinessData(factory, session.Session, new NoJs());
using var otherData = new BusinessData(factory, otherSession.Session, new NoJs());
var service = new MetricsService(businessData); var otherService = new MetricsService(otherData);
var loaded = await service.LoadAsync();
Check(loaded.InventoryCost == metrics.InventoryCost && loaded.InventorySaleValue == metrics.InventorySaleValue && loaded.Units == 16, "Persisted data matches calculations");
Check((await otherService.LoadAsync()).ProductCount == 0, "Other user's empty business isolated");
await using (var db = factory.CreateDbContext())
{
    db.Products.Add(new() { BusinessId = otherBusiness.Id, Name = "Foreign expensive stock", Quantity = 100, Cost = 999, DesiredProfitPercent = 30 }); await db.SaveChangesAsync();
}
Check((await service.LoadAsync()).ProductCount == 5 && (await otherService.LoadAsync()).ProductCount == 1, "No cross-business metrics leakage");
await businessData.AdjustAsync(data[3].Product.Id, 4);
Check((await service.LoadAsync()).OutOfStock == 0 && (await service.LoadAsync()).Units == 20, "Stock corrections reflected");
var price = data[1].Price; price.ProductId = data[1].Product.Id; price.SalePrice = 250;
await businessData.SavePriceAsync(price);
Check((await service.LoadAsync()).ProductsSellingAtLoss == 0, "Price corrections reflected");
await using (var db = factory.CreateDbContext()) await db.AccountLoginSessions.Where(s => s.Id == session.Id).ExecuteDeleteAsync();
try { await service.LoadAsync(); throw new Exception("Revoked session accepted"); } catch (InvalidOperationException e) { Check(PersistenceErrors.IsStorageError(e), "Session failure handled by UI error path"); }
Console.WriteLine($"{checks} comprobaciones correctas: datos reales, cobertura, cálculos, límites, usuarios aislados, actualizaciones y errores.");
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path);
async Task<(AccountSession Session, Guid Id)> Session(Guid user)
{
    await using var db = factory.CreateDbContext();
    var login = new AccountLoginSession { UserId = user, ExpiresAt = DateTime.UtcNow.AddHours(1) }; db.AccountLoginSessions.Add(login); await db.SaveChangesAsync();
    var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString()), new Claim("sid", login.Id.ToString())], "test"));
    return (new AccountSession(new TestAuthentication(principal), accounts, factory), login.Id);
}
sealed class Factory(DbContextOptions<AquarellaDbContext> options) : IDbContextFactory<AquarellaDbContext> { public AquarellaDbContext CreateDbContext() => new(options); }
sealed class TestAuthentication(ClaimsPrincipal principal) : AuthenticationStateProvider { public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal)); }
sealed class NoEmail : IAccountEmail { public bool Available => false; public Task SendAsync(string recipient, string purpose, string link) => throw new Exception("No external calls expected"); }
sealed class NoJs : IJSRuntime {
    public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => throw new Exception("No browser import expected for an imported business");
    public ValueTask<T> InvokeAsync<T>(string id, CancellationToken cancellation, object?[]? args) => InvokeAsync<T>(id, args);
}
