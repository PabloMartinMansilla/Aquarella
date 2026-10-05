using Aquarella.Data;
using Aquarella.Models;
using Aquarella.Services;
using Aquarella.Services.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.JSInterop;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

var path = Path.Combine(Path.GetTempPath(), $"aquarella-stock-intake-{Guid.NewGuid():N}.db");
var factory = new Factory(new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite($"Data Source={path}").Options);
var accounts = new AccountService(factory, new EphemeralDataProtectionProvider(), new Email());
var user = new User { Username = "stock-test" }; var other = new User { Username = "stock-other" };
var sessionRow = new AccountLoginSession { UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddHours(1) };
var otherSession = new AccountLoginSession { UserId = other.Id, ExpiresAt = DateTime.UtcNow.AddHours(1) };
await using (var db = factory.CreateDbContext())
{
    await db.Database.MigrateAsync();
    Check(!db.Database.HasPendingModelChanges(), "migration snapshot matches runtime model");
    db.Users.AddRange(user, other); db.Businesses.AddRange(new Business { User = user, LegacyImported = true }, new Business { User = other, LegacyImported = true });
    db.AccountLoginSessions.AddRange(sessionRow, otherSession); await db.SaveChangesAsync();
}
var session = Session(user.Id, sessionRow.Id);
var data = new BusinessData(factory, session, new Js());
var otherData = new BusinessData(factory, Session(other.Id, otherSession.Id), new Js());
var coca = Guid.NewGuid();
await data.SaveProductAsync(new(coca, "Coca Cola 2,25 L", 8), true);
await data.SavePriceAsync(new() { ProductId = coca, Cost = 1200, DesiredProfitPercent = 40, ManualSalePrice = true, SalePrice = 3000 });
var first = await data.ApplyStockIntakeAsync([new("  coca-cola  2.25 l ", 6)], "manual:one");
Check(!first.Lines.Single().Created && first.Lines.Single().FinalQuantity == 14, "case, spacing, punctuation and decimal normalization add 8+6=14");
Check((await data.LoadProductsAsync()).Single().Name == "Coca Cola 2,25 L", "preserve original product name");
var unchanged = (await data.LoadPricesAsync()).Single();
Check(unchanged.Cost == 1200 && unchanged.SalePrice == 3000 && unchanged.DesiredProfitPercent == 40 && unchanged.ManualSalePrice, "manual intake preserves pricing");
Check((await data.ApplyStockIntakeAsync([new("ignore retry payload", 99)], "manual:one")).AlreadyApplied, "receipt replay is idempotent");
Check((await data.LoadProductsAsync()).Single().Quantity == 14, "retry does not double stock");
await data.ApplyStockIntakeAsync([new("Coca Cola 1,5 L", 3)], "manual:different-size");
Check((await data.LoadProductsAsync()).Count == 2, "different package size is not fused");
await data.SaveProductAsync(new(coca, "Coca Cola 2,25 L", 10), false);
Check((await data.LoadProductsAsync()).Single(p => p.Id == coca).Quantity == 10, "explicit edit replaces quantity");
var invoice = await data.ApplyStockIntakeAsync([new("COCA COLA 2,25 L", 4, 3200), new("Sprite 2,25 L", 2, 2900), new("sprite 2,25 l", 3)], "invoice:fixture");
Check(invoice.Lines.Count == 3 && invoice.Lines[1].Created && !invoice.Lines[2].Created && invoice.Lines[2].FinalQuantity == 5, "mixed invoice and within-batch duplicates");
var updated = (await data.LoadPricesAsync()).Single(p => p.ProductId == coca);
Check(updated.Cost == 3200 && updated.DesiredProfitPercent == 40 && updated.SalePrice == 3000 && updated.ManualSalePrice, "invoice updates only explicit cost, preserving manual price and margin");
var automatic = Guid.NewGuid(); await data.SaveProductAsync(new(automatic, "Auto price", 1), true);
await data.SavePriceAsync(new() { ProductId = automatic, Cost = 100, DesiredProfitPercent = 25, SalePrice = 125 });
await data.ApplyStockIntakeAsync([new("Auto price", 2, 120)], "invoice:auto");
Check((await data.LoadPricesAsync()).Single(p => p.ProductId == automatic).SalePrice == 150, "automatic price recalculates from new cost and existing margin");
var before = await data.LoadProductsAsync();
foreach (var line in new[] { new StockIntakeLine("", 1), new("bad", -1), new("bad", 1, -1), new("bad", 1, 1_000_000_001m), new("Coca Cola 2,25 L", int.MaxValue) })
    await Reject(() => data.ApplyStockIntakeAsync([new("Must roll back", 1), line], "invalid:" + Guid.NewGuid()), "invalid batch rejected atomically");
Check(!(await data.LoadProductsAsync()).Any(p => p.Name == "Must roll back") && (await data.LoadProductsAsync()).Count == before.Count, "invalid batch writes nothing");
var foreign = Guid.NewGuid(); await otherData.SaveProductAsync(new(foreign, "Foreign", 7), true);
await Reject(() => data.ApplyStockIntakeAsync([new("Foreign", 1, ExistingId: foreign)], "foreign:id"), "cross-business association rejected");
await data.ApplyStockIntakeAsync([new("Foreign", 1)], "foreign:name");
Check((await otherData.LoadProductsAsync()).Single().Quantity == 7, "cross-business names do not match");
Check(!(await otherData.ApplyStockIntakeAsync([new("Other receipt", 1)], "manual:one")).AlreadyApplied, "receipt scoped per business");
var ambiguous = Guid.NewGuid(); await data.SaveProductAsync(new(ambiguous, "COCA COLA 2.25 L", 1), true);
await Reject(() => data.ApplyStockIntakeAsync([new("Coca Cola 2,25 L", 1)], "ambiguous:auto"), "ambiguous duplicate names never fuse automatically");
await data.ApplyStockIntakeAsync([new("Coca Cola", 1, ExistingId: coca)], "ambiguous:explicit");
await data.ApplyStockIntakeAsync([new("Coca Cola 2,25 L", 1, CreateNew: true)], "ambiguous:new");
var concurrentId = Guid.NewGuid(); await data.SaveProductAsync(new(concurrentId, "Concurrency", 0), true);
await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => new BusinessData(factory, session, new Js()).ApplyStockIntakeAsync([new("Concurrency", 20)], "invoice:concurrent-same"))));
Check((await data.LoadProductsAsync()).Single(p => p.Id == concurrentId).Quantity == 20, "concurrent duplicate confirmations apply once");
await Task.WhenAll(Enumerable.Range(0, 2).Select(i => Task.Run(() => new BusinessData(factory, session, new Js()).ApplyStockIntakeAsync([new("Concurrency", 2)], "manual:concurrent-" + i))));
Check((await new BusinessData(factory, session, new Js()).LoadProductsAsync()).Single(p => p.Id == concurrentId).Quantity == 24, "concurrent distinct loads and persistence");
Check(InvoiceDocument.DetectType("%PDF-fixture"u8.ToArray()) == "application/pdf", "PDF signature");
Check(InvoiceDocument.DetectType([255,216,255]) == "image/jpeg", "JPEG signature");
Check(InvoiceDocument.DetectType([137,80,78,71,13,10,26,10]) == "image/png", "PNG signature");
Check(InvoiceDocument.DetectType("RIFFxxxxWEBP"u8.ToArray()) == "image/webp", "WebP signature");
try { InvoiceDocument.DetectType("not an invoice"u8.ToArray()); throw new Exception("invalid file accepted"); } catch (InvoiceExtractionException) { }
try { await new UnavailableInvoiceExtractor().ExtractAsync(Stream.Null, "application/pdf", default); throw new Exception("fictitious OCR"); } catch (InvoiceExtractionException) { }
await using (var db = factory.CreateDbContext()) { await db.AccountLoginSessions.Where(s => s.Id == sessionRow.Id).ExecuteDeleteAsync(); }
try { await data.ApplyStockIntakeAsync([new("revoked", 1)], "revoked"); throw new Exception("revoked session accepted"); } catch (InvalidOperationException) { }
var anonymous = new AccountSession(new Auth(new()), accounts, factory);
try { await new BusinessData(factory, anonymous, new Js()).ApplyStockIntakeAsync([new("anonymous", 1)], "anonymous"); throw new Exception("anonymous accepted"); } catch (InvalidOperationException) { }
Console.WriteLine("PASS: additive migration, conservative matching, quantities, edit/add separation, mixed batches, cost/price preservation, atomic rollback, persisted idempotence, concurrent submits, business isolation, revoked/anonymous users, document validation and explicit unavailable OCR.");
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path);
AccountSession Session(Guid id, Guid sid) => new(new Auth(new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim("sid", sid.ToString())], "test"))), accounts, factory);
static void Check(bool value, string label) { if (!value) throw new Exception(label); }
static async Task Reject(Func<Task> action, string label) { try { await action(); throw new Exception(label); } catch (ValidationException) { } }
sealed class Factory(DbContextOptions<AquarellaDbContext> options) : IDbContextFactory<AquarellaDbContext> { public AquarellaDbContext CreateDbContext() => new(options); }
sealed class Auth(ClaimsPrincipal principal) : AuthenticationStateProvider { public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal)); }
sealed class Email : IAccountEmail { public bool Available => true; public Task SendAsync(string recipient, string purpose, string link) => Task.CompletedTask; }
sealed class Js : IJSRuntime { public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => throw new Exception("No browser import expected"); public ValueTask<T> InvokeAsync<T>(string id, CancellationToken token, object?[]? args) => InvokeAsync<T>(id,args); }
