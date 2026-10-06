using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Aquarella.Data;
using Aquarella.Models;
using Aquarella.Services;
using Aquarella.Services.Accounts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

const string password = "Disposable audit fixture 123!";
if (args.Length < 2)
{
    var root = Path.Combine(Path.GetTempPath(), "aquarella-data-audit-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var database = Path.Combine(root, "audit.db");
    Console.WriteLine("Isolated audit artifacts: " + root);
    await Child("seed", database);
    if (args.Length == 1) await Child(args[0], database);
    else { await Child("audit", database); await Child("read", database); await Child("read", Path.Combine(root, "backup.db")); }
    Console.WriteLine("PASS: isolated process boundaries; artifacts: " + root);
    return;
}
var phase = args[0];
var path = Path.GetFullPath(args[1]);
var folder = Path.GetDirectoryName(path)!;
if (!folder.StartsWith(Path.Combine(Path.GetTempPath(), "aquarella-data-audit-"), StringComparison.OrdinalIgnoreCase)
    || Path.GetFileName(path) is not ("audit.db" or "backup.db")) throw new Exception("Only this verifier's temporary databases are allowed.");
var factory = new Factory(new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite($"Data Source={path}").Options);
var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder, "keys")), b => b.SetApplicationName("Aquarella.PersistenceVerification"));
var accounts = new AccountService(factory, protection, new NoEmail());
var changes = new DatabaseChanges();
using var httpServices = new ServiceCollection().AddSingleton<IAuthenticationService, FixtureAuthentication>().BuildServiceProvider();

if (phase == "seed")
{
    await using (var db = factory.CreateDbContext())
    {
        await db.Database.MigrateAsync();
        Check(!db.Database.HasPendingModelChanges(), "Migration snapshot matches EF model");
        Check((await db.Database.GetAppliedMigrationsAsync()).Count() == 4, "All four migrations build a fresh database");
    }
    foreach (var suffix in new[] { "A", "B" })
    {
        var result = await accounts.RegisterAsync("Audit", suffix, Address(suffix), password);
        Check(result.User is not null, "Real account registration " + suffix);
        if (suffix == "A") await File.WriteAllTextAsync(Path.Combine(folder, "development-user.txt"), result.User!.Username);
        var token = await accounts.IssueTokenAsync(result.User!.Id, "verify");
        Check(await accounts.ConsumeAsync(token, "verify"), "Verified account " + suffix);
        var (data, session, _) = await Login(suffix);
        using (data)
        {
            await SaveProfile(data, new() { Name = "Negocio " + suffix, Description = "Descripción " + suffix, Industry = "Rubro " + suffix,
                Hours = "Horario " + suffix, PrimaryColor = suffix == "A" ? "#F4EFE9" : "#EDF5EF", LogoDataUrl = "data:image/png;base64,aGVsbG8=" });
            var product = (await data.ApplyStockIntakeAsync([new("Producto Exclusivo " + suffix, suffix == "A" ? 17 : 31)], "seed")).Lines.Single();
            await data.ApplyPriceEditAsync(new(product.ProductId, PriceField.Cost, suffix == "A" ? 2000.50m : 20.25m));
            await data.ApplyPriceEditAsync(new(product.ProductId, PriceField.DesiredProfitPercent, suffix == "A" ? 35m : 80m));
            await data.ApplyPriceEditAsync(new(product.ProductId, PriceField.SalePrice, suffix == "A" ? 3000.75m : 50.10m));
            await data.SaveNoteAsync(new(Guid.NewGuid(), new(2026, 10, 15), "Evento Agenda " + suffix, "Contenido " + suffix, suffix == "A" ? null : "15:30"));
            await new AiSettingsStore(factory, session).SaveAsync(new() { AiCustomInstructions = "Preferencia " + suffix, AiResponseTone = suffix == "A" ? AiResponseTone.Direct : AiResponseTone.Professional });
            if (suffix == "A") await session.CompleteTutorialAsync();
        }
    }
    Console.WriteLine("PASS seed: real registration, verified users, server sessions and all persistent business/preferences data.");
    return;
}
if (phase == "read")
{
    foreach (var suffix in new[] { "A", "B" }) await Read(suffix);
    Console.WriteLine("PASS read: profile/logo/colors, stock, exact prices, agenda, AI preferences, tutorial and receipts survive a new process or backup restore.");
    return;
}
var (a, sessionA, contextA) = await Login("A");
var (b, sessionB, contextB) = await Login("B");
using (a) using (b)
{
    var productA = (await a.LoadProductsAsync()).Single();
    var productB = (await b.LoadProductsAsync()).Single();
    var noteB = (await b.LoadNotesAsync()).Single();
    var beforeB = await Snapshot(b, sessionB);
    if (phase == "probe-profile") { await InvalidProfile(); return; }
    if (phase == "probe-session") { await MixedSessionLogout(); return; }
    if (phase == "probe-receipt") { await InvalidReceipt(); return; }
    Check(phase == "audit", "Known test phase");
    await Read("A"); await Read("B");
    await Reject(() => a.SaveProductAsync(productB with { Name = "Attack", Quantity = 999 }, false), "Foreign product edit");
    await Reject(() => a.SaveProductAsync(productB with { Name = "Attack" }, true), "Foreign product ID collision on creation");
    await Reject(() => a.AdjustAsync(productB.Id, 100), "Foreign stock adjustment");
    await Reject(() => a.ApplyPriceEditAsync(new(productB.Id, PriceField.Cost, 999)), "Foreign price patch");
    await Reject(() => a.SavePriceAsync(new() { ProductId = productB.Id, Cost = 999 }), "Foreign full price replacement");
    await Reject(() => a.SaveNoteAsync(noteB with { Title = "Attack" }), "Foreign note edit/ID collision");
    await Reject(() => a.ApplyStockIntakeAsync([new("Attack", 1, ExistingId: productB.Id)], "foreign-link"), "Foreign stock association");
    await a.DeleteProductAsync(productB.Id); await a.DeleteNoteAsync(noteB.Id);
    Check(await Snapshot(b, sessionB) == beforeB, "All forged-ID actions preserve B exactly, including silent no-op deletes");
    // Profile has no client-supplied owner ID: copying values can only update A's own row.
    await SaveProfile(a, new() { Name = "Own edit, never B" });
    Check(await Snapshot(b, sessionB) == beforeB, "Profile save cannot select another business");
    await SaveProfile(a, new() { Name = "Negocio A", Description = "Descripción A", Industry = "Rubro A", Hours = "Horario A", PrimaryColor = "#F4EFE9", LogoDataUrl = "data:image/png;base64,aGVsbG8=" });
    await InvalidProfile(); await MixedSessionLogout();
    Console.WriteLine("PASS isolation: scoped reads, forged product/note IDs, creation collisions, stock association, profile ownership and wrong user/session pair.");

    await using (var db = factory.CreateDbContext())
    {
        await db.Database.OpenConnectionAsync();
        Check(Convert.ToInt32(await Scalar(db, "PRAGMA foreign_keys")) == 1, "Foreign keys enabled on actual connection");
        Check((string)(await Scalar(db, "PRAGMA integrity_check"))! == "ok", "SQLite integrity check");
        await Reject(() => db.Database.ExecuteSqlRawAsync("INSERT INTO Products (Id,BusinessId,Name,Quantity,Cost,DesiredProfitPercent,SalePrice,ManualSalePrice,CreatedAt,UpdatedAt) VALUES ('bad-fk','missing','orphan',1,'0','0','0',0,'2026-10-05','2026-10-05')"), "Foreign key prevents orphan");
        Check(await Scalar(db, "PRAGMA journal_mode") is string mode && mode == "wal", "Migration-created SQLite uses WAL");
        Console.WriteLine("PASS SQLite: FK enabled, integrity ok, WAL and no orphan insertion.");
    }
    var testProduct = Guid.NewGuid();
    await a.SaveProductAsync(new(testProduct, "Delete-only fixture", 0), true);
    await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => a.AdjustAsync(testProduct, 1)));
    Check((await a.LoadProductsAsync()).Single(p => p.Id == testProduct).Quantity == 8, "Concurrent atomic stock increments add exactly");
    await Reject(() => a.AdjustAsync(testProduct, -9), "Negative quantity");
    await a.SaveProductAsync(new(testProduct, "Delete-only fixture", int.MaxValue), false);
    await Reject(() => a.AdjustAsync(testProduct, 1), "Integer overflow guard");
    foreach (var amount in new[] { 0m, 2m, 20m, 200m, 2000m, 20000m, 2000.50m, 1_000_000_000m })
    {
        await a.ApplyPriceEditAsync(new(testProduct, PriceField.Cost, amount));
        Check((await a.LoadPricesAsync()).Single(p => p.ProductId == testProduct).Cost == amount, "Exact decimal persistence " + amount);
    }
    await a.ApplyPriceEditAsync(new(testProduct, PriceField.DesiredProfitPercent, 10_000m));
    await Reject(() => a.ApplyPriceEditAsync(new(testProduct, PriceField.DesiredProfitPercent, 10_001m)), "Percent upper bound");
    await Reject(() => a.ApplyPriceEditAsync(new(testProduct, PriceField.Cost, -1)), "Negative monetary value");
    await Reject(() => a.SaveProductAsync(new(Guid.NewGuid(), "", 1), true), "Empty name");

    var baselineQuantity = (await a.LoadProductsAsync()).Single(p => p.Id == productA.Id).Quantity;
    await using (var db = factory.CreateDbContext())
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER audit_abort BEFORE INSERT ON StockIntakeReceipts WHEN NEW.OperationKey='forced-failure' BEGIN SELECT RAISE(ABORT, 'isolated receipt failure'); END");
    await Reject(() => a.ApplyStockIntakeAsync([new(productA.Name, 5, 100), new("Must not persist", 1)], "forced-failure"), "Failure after product writes rolls back entire intake");
    Check((await a.LoadProductsAsync()).Single(p => p.Id == productA.Id).Quantity == baselineQuantity && !(await a.LoadProductsAsync()).Any(p => p.Name == "Must not persist"), "Rollback retains quantities and excludes partial creation");
    Check((await a.LoadPricesAsync()).Single(p => p.ProductId == productA.Id).Cost == 2000.50m, "Rollback retains original cost");
    await using (var db = factory.CreateDbContext())
    {
        Check(!await db.StockIntakeReceipts.AnyAsync(r => r.OperationKey == "forced-failure"), "No false success receipt");
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER audit_abort");
    }
    await InvalidReceipt();
    await a.DeleteProductAsync(testProduct);
    Check(!(await a.LoadProductsAsync()).Any(p => p.Id == testProduct) && !(await a.LoadPricesAsync()).Any(p => p.ProductId == testProduct), "Delete removes quantity and price together (same row)");
    var testNote = new AgendaNote(Guid.NewGuid(), new(2026, 11, 1), "Delete note", "", null);
    await a.SaveNoteAsync(testNote); await a.SaveNoteAsync(testNote with { Title = "Edited note" }); await a.DeleteNoteAsync(testNote.Id);
    Check(!(await a.LoadNotesAsync()).Any(n => n.Id == testNote.Id), "Note create/edit/delete persists");
    Check(await Snapshot(b, sessionB) == beforeB, "Integrity/delete tests preserve other business");
    await ProfileConcurrency();

    // Directly exercise the declared cascades and a legacy import with foreign IDs.
    var c = new User { Username = "cascade-only" }; var cb = new Business { User = c };
    var cs = new AccountLoginSession { UserId = c.Id, ExpiresAt = DateTime.UtcNow.AddHours(1) };
    await using (var db = factory.CreateDbContext()) { db.Businesses.Add(cb); db.AccountLoginSessions.Add(cs); await db.SaveChangesAsync(); }
    var legacySession = new AccountSession(new Auth(Principal(c.Id, cs.Id)), accounts, factory);
    using (var legacy = new BusinessData(factory, legacySession, new LegacyJs(new() { Profile = new() { Name = "Must roll back" }, Products = [new(productB.Id, "Foreign collision", 999)], Notes = [new(Guid.NewGuid(), new(2026, 10, 15), "Must roll back", "", null)] })))
        await Reject(() => legacy.EnsureImportedAsync(), "Foreign legacy ID collision rejects entire import");
    await using (var db = factory.CreateDbContext())
    {
        Check(!await db.Businesses.Where(x => x.Id == cb.Id).Select(x => x.LegacyImported).SingleAsync(), "Failed legacy import remains retryable");
        Check((await db.Businesses.SingleAsync(x => x.Id == cb.Id)).Profile.Name == "Aquarella" && !await db.CalendarEntries.AnyAsync(n => n.BusinessId == cb.Id), "Failed legacy import writes no partial profile/note");
        var cp = new Product { BusinessId = cb.Id, Name = "Cascade fixture", Quantity = 1 };
        db.Products.Add(cp); db.CalendarEntries.Add(new() { BusinessId = cb.Id, Title = "Cascade note", Date = new(2026, 10, 15) });
        db.StockIntakeReceipts.Add(new() { BusinessId = cb.Id, OperationKey = "cascade", ResultsJson = "[]" });
        db.AccountTokens.Add(new() { UserId = c.Id, Purpose = "cascade", ExpiresAt = DateTime.UtcNow.AddHours(1) }); await db.SaveChangesAsync();
        await db.Users.Where(x => x.Id == c.Id).ExecuteDeleteAsync();
        Check(!await db.Businesses.AnyAsync(x => x.Id == cb.Id) && !await db.Products.AnyAsync(x => x.BusinessId == cb.Id)
            && !await db.CalendarEntries.AnyAsync(x => x.BusinessId == cb.Id) && !await db.StockIntakeReceipts.AnyAsync(x => x.BusinessId == cb.Id)
            && !await db.AccountTokens.AnyAsync(x => x.UserId == c.Id) && !await db.AccountLoginSessions.AnyAsync(x => x.UserId == c.Id), "User deletion cascades all dependent records");
    }
    Check(await Snapshot(b, sessionB) == beforeB, "Cascades and failed legacy import preserve B exactly");
    Console.WriteLine("PASS relationships: legacy foreign-ID rollback and declared User/Business cascades without cross-business deletion.");
    Console.WriteLine("PASS integrity: concurrent quantities, numeric limits, transaction rollback with a real failing trigger, receipt recovery and deletions.");

    // Every login gets a fresh scope; logout invalidates the old server session.
    await accounts.SignOutAsync(contextA);
    await Reject(() => a.LoadProductsAsync(), "Revoked old session");
    Check(await accounts.IsSessionValidAsync(contextB.User), "A logout cannot revoke B");
    await Read("B"); await Read("A"); await Read("B");
    var expired = new AccountLoginSession { UserId = Guid.Parse(contextA.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? sessionA.UserId!), ExpiresAt = DateTime.UtcNow.AddSeconds(-1) };
    await using (var db = factory.CreateDbContext()) { db.AccountLoginSessions.Add(expired); await db.SaveChangesAsync(); }
    foreach (var principal in new[] { Principal(expired.UserId, expired.Id), Principal(Guid.NewGuid(), Guid.NewGuid()), new ClaimsPrincipal() })
    {
        using var invalid = new BusinessData(factory, new(new Auth(principal), accounts, factory), new NoJs());
        await Reject(() => invalid.LoadProductsAsync(), "Expired/missing/anonymous session");
        await Reject(() => invalid.SaveNoteAsync(new(Guid.NewGuid(), new(2026, 10, 15), "Attack", "", null)), "Invalid session write");
    }
    Console.WriteLine("PASS users: login/logout, fresh A/B scopes, expiry, nonexistent users and anonymous write denial.");
    // Backup the live test database through SQLite's coherent backup API, not by copying the main WAL file.
    await using var source = new SqliteConnection($"Data Source={path}"); await source.OpenAsync();
    await using var destination = new SqliteConnection($"Data Source={Path.Combine(folder, "backup.db")}"); await destination.OpenAsync(); source.BackupDatabase(destination);
    Console.WriteLine("PASS backup: SQLite online backup created; a separate child will verify restored business data.");

    async Task ProfileConcurrency()
    {
        var restore = await a.LoadProfileAsync();
        var initial = new BusinessProfile { Name = "Kiosco Pablo", Phone = "351111111" };
        async Task<(BusinessProfileStore, BusinessProfileStore)> Tabs()
        {
            await SaveProfile(a, initial);
            var first = new BusinessProfileStore(new DatabaseBusinessProfilePersistence(a, new NoJs()));
            var second = new BusinessProfileStore(new DatabaseBusinessProfilePersistence(a, new NoJs()));
            await first.InitializeAsync(); await second.InitializeAsync();
            return (first, second);
        }
        var (first, second) = await Tabs();
        var x = first.Load(); var y = second.Load();
        x.Name = "Kiosco Central"; await first.SaveAsync(x);
        y.Phone = "352222222"; await second.SaveAsync(y);
        var result = await a.LoadProfileAsync();
        Check(result.Name == "Kiosco Central" && result.Phone == "352222222", "Stale tab: Name + Phone both survive");
        Check(second.Load().Name == result.Name, "Store receives the actual merged profile");
        (first, second) = await Tabs(); x = first.Load(); y = second.Load();
        x.Phone = "352222222"; await first.SaveAsync(x);
        y.Name = "Kiosco Central"; await second.SaveAsync(y);
        result = await a.LoadProfileAsync();
        Check(result.Name == "Kiosco Central" && result.Phone == "352222222", "Inverse stale tab: Phone + Name both survive");
        (first, second) = await Tabs(); x = first.Load(); y = second.Load();
        x.Name = "Kiosco Central"; await first.SaveAsync(x);
        y.Name = "Kiosco Norte"; await second.SaveAsync(y);
        Check((await a.LoadProfileAsync()).Name == "Kiosco Norte", "Same field: last valid save wins");
        var invalidName = second.Load(); invalidName.Name = "";
        await Reject(() => second.SaveAsync(invalidName), "Invalid later same-field edit rejected");
        Check((await a.LoadProfileAsync()).Name == "Kiosco Norte", "Invalid edit does not win over the last valid name");
        (first, second) = await Tabs(); x = first.Load(); y = second.Load();
        x.Name = "Concurrent name"; y.Phone = "Concurrent phone";
        await Task.WhenAll(first.SaveAsync(x), second.SaveAsync(y));
        result = await a.LoadProfileAsync();
        Check(result.Name == x.Name && result.Phone == y.Phone, "Actually concurrent different-field saves survive");
        (first, second) = await Tabs(); x = first.Load(); y = second.Load();
        x.PrimaryColor = "#abcdef"; x.LogoDataUrl = "data:image/png;base64,aGVsbG8="; await first.SaveAsync(x);
        y.PrimaryColor = y.PrimaryColor.ToLowerInvariant(); y.Description = "Details";
        y.Email = "business@example.test"; y.Website = "https://example.test";
        y.Instagram = y.Facebook = y.TikTok = y.X = y.LinkedIn = y.YouTube = "https://example.test/social";
        y.Industry = "Kiosco"; y.Hours = "9–18"; y.SecondaryColor = "#112233"; y.TertiaryColor = "#445566";
        await second.SaveAsync(y); result = await a.LoadProfileAsync();
        Check(result.PrimaryColor == x.PrimaryColor && result.LogoDataUrl == x.LogoDataUrl && result.Description == y.Description
            && result.Email == y.Email && result.Website == y.Website && result.Instagram == y.Instagram && result.Facebook == y.Facebook
            && result.TikTok == y.TikTok && result.X == y.X && result.LinkedIn == y.LinkedIn && result.YouTube == y.YouTube
            && result.Industry == y.Industry && result.Hours == y.Hours && result.SecondaryColor == y.SecondaryColor && result.TertiaryColor == y.TertiaryColor,
            "All other fields and HEX representation preserve independent edits");
        var clear = second.Load(); clear.LogoDataUrl = null; clear.Description = ""; await second.SaveAsync(clear);
        Check((await a.LoadProfileAsync()).LogoDataUrl is null && (await a.LoadProfileAsync()).Description == "", "Explicit optional clearing persists");
        var invalid = second.Load(); invalid.Email = "invalid";
        await Reject(() => second.SaveAsync(invalid), "Invalid partial profile rejected");
        Check((await a.LoadProfileAsync()).Email == "business@example.test", "Rejected patch retains valid persisted fields");
        var unchanged = second.Load();
        var otherName = unchanged.Copy(); otherName.Name = "Newer name";
        await a.SaveProfileAsync(otherName, unchanged);
        await second.SaveAsync(unchanged);
        Check(second.Load().Name == "Newer name", "Unchanged stale form preserves latest fields and refreshes its baseline");
        await using (var db = factory.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER audit_profile_abort BEFORE UPDATE ON Businesses WHEN NEW.Profile_Phone='fail-profile-fixture' BEGIN SELECT RAISE(ABORT, 'isolated profile failure'); END");
        var beforeFailure = second.Load(); var failing = beforeFailure.Copy(); failing.Phone = "fail-profile-fixture";
        await Reject(() => second.SaveAsync(failing), "SQL profile failure controlled");
        Check(JsonSerializer.Serialize(second.Load()) == JsonSerializer.Serialize(beforeFailure)
            && JsonSerializer.Serialize(await a.LoadProfileAsync()) == JsonSerializer.Serialize(beforeFailure), "Failed profile save preserves store baseline and database");
        await using (var db = factory.CreateDbContext()) await db.Database.ExecuteSqlRawAsync("DROP TRIGGER audit_profile_abort");
        failing.Phone = "361111111"; await second.SaveAsync(failing);
        Check(second.Load().Phone == "361111111" && second.Load().Name == "Newer name", "Retry persists only intended fields");
        var foreignOriginal = await b.LoadProfileAsync();
        await a.SaveProfileAsync(second.Load(), foreignOriginal);
        Check(await Snapshot(b, sessionB) == beforeB, "Even an original profile copied from B cannot select or write B's row");
        await SaveProfile(a, restore);
        Console.WriteLine("PASS profile concurrency: stale tabs in both orders, same field, true concurrency, all editable fields, clearing, validation, unchanged saves, SQL failure/retry and ownership.");
    }

    async Task InvalidProfile()
    {
        var previous = await a.LoadProfileAsync();
        await Reject(() => SaveProfile(a, new() { Name = "", PrimaryColor = "invalid" }), "Profile boundary rejects invalid direct service writes");
        Check(JsonSerializer.Serialize(await a.LoadProfileAsync()) == JsonSerializer.Serialize(previous), "Rejected profile leaves original unchanged");
        Console.WriteLine("PASS profile boundary.");
    }
    async Task MixedSessionLogout()
    {
        var bad = new DefaultHttpContext { RequestServices = httpServices, User = Principal(Guid.Parse(sessionA.UserId!), Guid.Parse(contextB.User.FindFirstValue("sid")!)) };
        Check(!await accounts.IsSessionValidAsync(bad.User), "A identity plus B session denied");
        using var invalid = new BusinessData(factory, new(new Auth(bad.User), accounts, factory), new NoJs());
        await Reject(() => invalid.LoadProductsAsync(), "Wrong owner session cannot read");
        await accounts.SignOutAsync(bad);
        Check(await accounts.IsSessionValidAsync(contextB.User), "Logout with mismatched identity cannot delete B session");
        var anonymousClaims = new ClaimsPrincipal(new ClaimsIdentity(contextB.User.Claims));
        Check(!await accounts.IsSessionValidAsync(anonymousClaims), "Valid-looking claims on an unauthenticated identity must still be denied");
        await accounts.SignOutAsync(new DefaultHttpContext { RequestServices = httpServices, User = anonymousClaims });
        Check(await accounts.IsSessionValidAsync(contextB.User), "Unauthenticated logout cannot revoke a valid session by copied claims");
        Console.WriteLine("PASS paired session logout.");
    }
    async Task InvalidReceipt()
    {
        var outcome = await a.ApplyStockIntakeAsync([new("Receipt-only fixture", 0)], "corrupt-receipt");
        await using var db = factory.CreateDbContext();
        var receipt = await db.StockIntakeReceipts.SingleAsync(r => r.OperationKey == "corrupt-receipt");
        var original = receipt.ResultsJson;
        foreach (var malformed in new[] { "null", "broken-json", "[null]", "[]" })
        {
            receipt.ResultsJson = malformed; await db.SaveChangesAsync();
            await Reject(() => a.ApplyStockIntakeAsync([new("Receipt-only fixture", 100)], "corrupt-receipt"), "Malformed saved receipt is controlled, never reapplied");
            Check((await a.LoadProductsAsync()).Single(p => p.Id == outcome.Lines.Single().ProductId).Quantity == 0, "Malformed replay never repeats an applied operation");
        }
        receipt.ResultsJson = original; await db.SaveChangesAsync();
        Check((await a.ApplyStockIntakeAsync([new("ignored", 1)], "corrupt-receipt")).AlreadyApplied, "Repaired receipt replays normally");
        await a.DeleteProductAsync(outcome.Lines.Single().ProductId);
        Check(await db.StockIntakeReceipts.AnyAsync(r => r.Id == receipt.Id), "Product deletion intentionally retains receipt history/idempotence");
        Console.WriteLine("PASS receipt malformed replay and retained confirmation history.");
    }
}

async Task<(BusinessData Data, AccountSession Session, HttpContext Context)> Login(string suffix)
{
    var user = await accounts.AuthenticateAsync(Address(suffix), password) ?? throw new Exception("Fixture authentication failed");
    var context = new DefaultHttpContext { RequestServices = httpServices };
    await accounts.SignInAsync(context, user, true);
    var session = new AccountSession(new Auth(context.User), accounts, factory);
    await session.InitializeAsync();
    return (new(factory, session, new NoJs(), changes), session, context);
}
async Task Read(string suffix)
{
    var (data, session, _) = await Login(suffix);
    using (data)
    {
        var profileStore = new BusinessProfileStore(new DatabaseBusinessProfilePersistence(data, new NoJs()));
        await profileStore.InitializeAsync();
        var product = (await data.LoadProductsAsync()).Single(); var price = (await data.LoadPricesAsync()).Single(); var profile = profileStore.Load();
        Check(product.Name == "Producto Exclusivo " + suffix && product.Quantity == (suffix == "A" ? 17 : 31), "Isolated stock " + suffix);
        Check(profile.Name == "Negocio " + suffix && profile.Description == "Descripción " + suffix && profile.Hours == "Horario " + suffix && profile.LogoDataUrl == "data:image/png;base64,aGVsbG8=", "Profile details and logo " + suffix);
        Check(profile.PrimaryColor == (suffix == "A" ? "#F4EFE9" : "#EDF5EF"), "Own page colors " + suffix);
        Check(price.Cost == (suffix == "A" ? 2000.50m : 20.25m) && price.SalePrice == (suffix == "A" ? 3000.75m : 50.10m)
            && price.DesiredProfitPercent == (suffix == "A" ? 35m : 80m) && price.ManualSalePrice, "Exact persisted pricing " + suffix);
        var note = (await data.LoadNotesAsync()).Single();
        Check(note.Title == "Evento Agenda " + suffix && note.Content == "Contenido " + suffix && note.Date == new DateOnly(2026, 10, 15) && note.Time == (suffix == "A" ? null : "15:30"), "Isolated agenda " + suffix);
        Check((await new AiSettingsStore(factory, session).LoadAsync()).AiCustomInstructions == "Preferencia " + suffix, "User preferences " + suffix);
        Check(await session.CompletedTutorialAsync() == (suffix == "A"), "Own tutorial flag " + suffix);
        Check((await new MetricsService(data).LoadAsync()).Units == product.Quantity, "Derived metrics contain own stock only");
        await using var db = factory.CreateDbContext();
        var businessId = await db.Businesses.Where(x => x.UserId == Guid.Parse(session.UserId!)).Select(x => x.Id).SingleAsync();
        var receipt = await db.StockIntakeReceipts.SingleAsync(x => x.BusinessId == businessId && x.OperationKey == "seed");
        Check(JsonSerializer.Deserialize<List<StockIntakeResult>>(receipt.ResultsJson)!.Single().ProductId == product.Id, "Confirmation receipt belongs to own product/business");
    }
}
static async Task SaveProfile(BusinessData data, BusinessProfile profile) => await data.SaveProfileAsync(profile, await data.LoadProfileAsync());
static async Task<string> Snapshot(BusinessData data, AccountSession session) => JsonSerializer.Serialize(new { Profile = await data.LoadProfileAsync(), Products = await data.LoadProductsAsync(), Prices = await data.LoadPricesAsync(), Notes = await data.LoadNotesAsync(), Tutorial = await session.CompletedTutorialAsync() });
static string Address(string suffix) => "audit-" + suffix.ToLowerInvariant() + "@example.test";
static ClaimsPrincipal Principal(Guid userId, Guid sessionId) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim("sid", sessionId.ToString())], "test"));
static void Check(bool success, string label) { if (!success) throw new Exception(label); }
static async Task Reject(Func<Task> operation, string label)
{
    try { await operation(); }
    catch (Exception e) when (e is ValidationException or ArgumentException or InvalidOperationException or DbUpdateException or SqliteException) { return; }
    throw new Exception("Expected rejection: " + label);
}
static async Task<object?> Scalar(AquarellaDbContext db, string sql) { using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(); }
static async Task Child(string phase, string path)
{
    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location); start.ArgumentList.Add(phase); start.ArgumentList.Add(path);
    using var process = Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90)); }
    catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
    Console.Write(await output); Console.Error.Write(await errors);
    if (process.ExitCode != 0) throw new Exception("Isolated phase failed: " + phase);
}
sealed class Factory(DbContextOptions<AquarellaDbContext> options) : IDbContextFactory<AquarellaDbContext> { public AquarellaDbContext CreateDbContext() => new(options); }
sealed class Auth(ClaimsPrincipal principal) : AuthenticationStateProvider { public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal)); }
sealed class NoJs : IJSRuntime { public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => id == "aquarellaIdentity.display" ? ValueTask.FromResult(default(T)!) : throw new Exception("No legacy/browser import expected for real accounts"); public ValueTask<T> InvokeAsync<T>(string id, CancellationToken token, object?[]? args) => InvokeAsync<T>(id, args); }
sealed class LegacyJs(BusinessData.LegacyData legacy) : IJSRuntime
{
    public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => ValueTask.FromResult((T)(object)new LegacyModule(legacy));
    public ValueTask<T> InvokeAsync<T>(string id, CancellationToken token, object?[]? args) => InvokeAsync<T>(id, args);
}
sealed class LegacyModule(BusinessData.LegacyData legacy) : IJSObjectReference
{
    public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => ValueTask.FromResult((T)(object)legacy);
    public ValueTask<T> InvokeAsync<T>(string id, CancellationToken token, object?[]? args) => InvokeAsync<T>(id, args);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
sealed class NoEmail : IAccountEmail { public bool Available => false; public Task SendAsync(string recipient, string purpose, string link) => throw new Exception("No external email in tests"); }
sealed class FixtureAuthentication : IAuthenticationService
{
    public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
    public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) { context.User = principal; return Task.CompletedTask; }
    public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) { context.User = new(); return Task.CompletedTask; }
}
