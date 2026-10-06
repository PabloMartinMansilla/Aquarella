using Aquarella.Data;
using Aquarella.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using System.ComponentModel.DataAnnotations;
namespace Aquarella.Services;

// All operations validate the server session and scope queries to the authenticated user's business.
// DbContexts are short lived; no EF tracking context is retained in the Blazor circuit.
public sealed class BusinessData(IDbContextFactory<AquarellaDbContext> factory, Aquarella.Services.Accounts.AccountSession session, IJSRuntime js, DatabaseChanges? notifications = null, ILogger<BusinessData>? logger = null) : IDisposable
{
    private Task? import;
    private readonly DatabaseChanges changes = notifications ?? new();
    private Guid? currentBusinessId;
    private bool subscribed;
    private void OnChange(Guid id, string area) { if (id != currentBusinessId || !session.IsSignedIn) return; if (area == "products") ProductsChanged?.Invoke(); if (area == "notes") NotesChanged?.Invoke(); if (area is "products" or "prices") NoticesChanged?.Invoke(); }
    public void Dispose() { if (subscribed) changes.Changed -= OnChange; }
    public event Action? ProductsChanged;
    public event Action? NotesChanged;
    public event Action? NoticesChanged;
    public sealed class LegacyData {
        public BusinessProfile? Profile { get; set; }
        public List<StockProduct> Products { get; set; } = [];
        public List<ProductPrice> Prices { get; set; } = [];
        public List<AgendaNote> Notes { get; set; } = [];
    }
    private async Task<Guid> BusinessId(AquarellaDbContext db)
    {
        var userId = await session.RequireUserAsync();
        currentBusinessId = await db.Businesses.Where(b => b.UserId == userId).Select(b => b.Id).SingleAsync();
        if (!subscribed) { changes.Changed += OnChange; subscribed = true; }
        return currentBusinessId.Value;
    }
    public async Task EnsureImportedAsync()
    {
        try { await (import ??= Import()); }
        catch (Exception e) { import = null; logger?.LogWarning("Business import/read failed ({ErrorType}); original data was not replaced.", e.GetType().Name); throw; }
    }
    private async Task Import()
    {
        await using var db = await factory.CreateDbContextAsync();
        var id = await BusinessId(db);
        var business = await db.Businesses.SingleAsync(b => b.Id == id);
        if (business.LegacyImported) return;
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./legacy-import.js");
        var legacy = await module.InvokeAsync<LegacyData>("read");
        if (legacy is null || legacy.Products is null || legacy.Prices is null || legacy.Notes is null)
            throw new ValidationException("Los datos locales contienen colecciones inválidas. Se conservaron sin importar.");
        if (legacy.Profile is not null && !BusinessProfileStore.IsValid(legacy.Profile)) throw new ValidationException("El perfil local contiene datos inválidos; se conservó sin importar.");
        foreach (var p in legacy.Products) ValidateProduct(p);
        foreach (var p in legacy.Prices) ValidatePrice(p);
        foreach (var n in legacy.Notes) ValidateNote(n);
        await using var tx = await db.Database.BeginTransactionAsync();
        // Recheck under the transaction; another circuit may already have imported this business.
        await db.Entry(business).ReloadAsync();
        if (business.LegacyImported) return;
        if (legacy.Profile is not null) db.Entry(business.Profile).CurrentValues.SetValues(legacy.Profile);
        foreach (var p in legacy.Products) {
            var price = legacy.Prices.SingleOrDefault(x => x.ProductId == p.Id);
            db.Products.Add(new Product { Id = p.Id, BusinessId = id, Name = p.Name, Quantity = p.Quantity, Cost = price?.Cost ?? 0, DesiredProfitPercent = price?.DesiredProfitPercent ?? 0, SalePrice = price?.SalePrice ?? 0, ManualSalePrice = price?.ManualSalePrice ?? false });
        }
        foreach (var n in legacy.Notes) db.CalendarEntries.Add(new CalendarEntry { Id = n.Id, BusinessId = id, Date = n.Date, Time = n.Time, Title = n.Title, Content = n.Content });
        business.LegacyImported = true; business.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(); await tx.CommitAsync();
        // Original browser values remain untouched as a recovery copy, never read again for this business.
    }
    public async Task<BusinessProfile> LoadProfileAsync() {
        await EnsureImportedAsync(); await using var db = await factory.CreateDbContextAsync(); var id = await BusinessId(db);
        return (await db.Businesses.AsNoTracking().SingleAsync(b => b.Id == id)).Profile;
    }
    public async Task<BusinessProfile> SaveProfileAsync(BusinessProfile profile, BusinessProfile original) {
        if (profile is null || original is null || !BusinessProfileStore.IsValid(profile)) throw new ValidationException("El perfil contiene datos inválidos.");
        var edited = profile.Copy();
        var baseline = original.Copy();
        await EnsureImportedAsync(); await using var db = await factory.CreateDbContextAsync(); var id = await BusinessId(db);
        // A SQLite write transaction keeps read/merge/validation/commit together across circuits.
        await using var tx = await db.Database.BeginTransactionAsync();
        var b = await db.Businesses.SingleAsync(b => b.Id == id);
        var merged = BusinessProfileChanges.Merge(baseline, edited, b.Profile);
        if (!BusinessProfileStore.IsValid(merged)) throw new ValidationException("El perfil contiene datos inválidos.");
        db.Entry(b.Profile).CurrentValues.SetValues(merged);
        b.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return b.Profile.Copy();
    }
    public async Task<List<StockProduct>> LoadProductsAsync(CancellationToken cancellationToken = default) {
        await EnsureImportedAsync(); await using var db = await factory.CreateDbContextAsync(cancellationToken); var id = await BusinessId(db);
        return await db.Products.AsNoTracking().Where(p => p.BusinessId == id).OrderBy(p => p.CreatedAt).Select(p => new StockProduct(p.Id, p.Name, p.Quantity)).ToListAsync(cancellationToken);
    }
    public async Task<List<NoticeProduct>> LoadNoticeProductsAsync(CancellationToken cancellationToken = default) {
        await EnsureImportedAsync(); await using var db = await factory.CreateDbContextAsync(cancellationToken); var id = await BusinessId(db);
        return await db.Products.AsNoTracking().Where(p => p.BusinessId == id)
            .Select(p => new NoticeProduct(new StockProduct(p.Id, p.Name, p.Quantity), new ProductPrice {
                ProductId = p.Id, Cost = p.Cost, DesiredProfitPercent = p.DesiredProfitPercent,
                SalePrice = p.SalePrice, ManualSalePrice = p.ManualSalePrice
            })).ToListAsync(cancellationToken);
    }
    public async Task SaveProductAsync(StockProduct value, bool create) {
        ValidateProduct(value); await EnsureImportedAsync(); await using var db = await factory.CreateDbContextAsync(); var id = await BusinessId(db);
        var p = create ? new Product { Id = value.Id, BusinessId = id } : await db.Products.SingleAsync(p => p.Id == value.Id && p.BusinessId == id);
        if (create) db.Products.Add(p); p.Name = value.Name; p.Quantity = value.Quantity; p.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(); changes.Publish(id, "products");
    }
    public async Task AdjustAsync(Guid productId, int adjustment) {
        await EnsureImportedAsync(); await using var db = await factory.CreateDbContextAsync(); var id = await BusinessId(db);
        var changed = await db.Products.Where(p => p.Id == productId && p.BusinessId == id && (long)p.Quantity + adjustment >= 0 && (long)p.Quantity + adjustment <= int.MaxValue)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Quantity, p => p.Quantity + adjustment).SetProperty(p => p.UpdatedAt, DateTime.UtcNow));
        if (changed != 1) throw new ArgumentException("Cantidad final inválida o producto eliminado."); changes.Publish(id, "products");
    }
    public async Task<StockIntakeOutcome> ApplyStockIntakeAsync(IReadOnlyList<StockIntakeLine> lines, string operationKey)
    {
        if (string.IsNullOrWhiteSpace(operationKey) || operationKey.Length > 100) throw new ValidationException("Identificador de carga inválido.");
        await EnsureImportedAsync();
        await using var db = await factory.CreateDbContextAsync();
        var id = await BusinessId(db);
        // SQLite serializes writers; receipt and all product updates commit together.
        await using var tx = await db.Database.BeginTransactionAsync();
        var receipt = await db.StockIntakeReceipts.SingleOrDefaultAsync(r => r.BusinessId == id && r.OperationKey == operationKey);
        if (receipt is not null)
        {
            try
            {
                var results = System.Text.Json.JsonSerializer.Deserialize<List<StockIntakeResult>>(receipt.ResultsJson);
                if (results is null || results.Count is < 1 or > 200 || results.Any(r => r is null || r.ProductId == Guid.Empty
                    || string.IsNullOrWhiteSpace(r.Name) || r.AddedQuantity < 0 || r.FinalQuantity < r.AddedQuantity))
                    throw new System.Text.Json.JsonException("Invalid saved stock receipt.");
                return new(results, true);
            }
            catch (System.Text.Json.JsonException)
            {
                logger?.LogWarning("A saved stock receipt could not be read. Original data and confirmed stock were retained.");
                throw new ValidationException("Esta carga ya fue confirmada, pero su comprobante no se puede leer. No se volvió a aplicar. Revisá los productos antes de continuar.");
            }
        }
        var products = await db.Products.Where(p => p.BusinessId == id).ToListAsync();
        var plan = StockIntakePlanner.Plan(products.Select(p => new StockIntakeItem(p.Id, p.Name, p.Quantity)), lines);
        for (var i = 0; i < plan.Count; i++)
        {
            var change = plan[i];
            var product = products.SingleOrDefault(p => p.Id == change.ProductId);
            if (product is null)
            {
                product = new Product { Id = change.ProductId, BusinessId = id, Name = change.Name };
                products.Add(product); db.Products.Add(product);
            }
            product.Quantity = change.FinalQuantity;
            if (lines[i].UnitCost is decimal cost)
            {
                product.Cost = cost;
                if (!product.ManualSalePrice) product.SalePrice = decimal.Round(cost * (1 + product.DesiredProfitPercent / 100), 2, MidpointRounding.AwayFromZero);
            }
            product.UpdatedAt = DateTime.UtcNow;
        }
        db.StockIntakeReceipts.Add(new() { BusinessId = id, OperationKey = operationKey, ResultsJson = System.Text.Json.JsonSerializer.Serialize(plan) });
        await db.SaveChangesAsync(); await tx.CommitAsync();
        changes.Publish(id, "products");
        return new(plan, false);
    }
    public async Task DeleteProductAsync(Guid productId) {
        await using var db = await factory.CreateDbContextAsync(); var id = await BusinessId(db);
        await db.Products.Where(p => p.Id == productId && p.BusinessId == id).ExecuteDeleteAsync(); changes.Publish(id, "products");
    }
    public async Task<List<ProductPrice>> LoadPricesAsync(CancellationToken cancellationToken = default) {
        await EnsureImportedAsync(); await using var db = await factory.CreateDbContextAsync(cancellationToken); var id = await BusinessId(db);
        return await db.Products.AsNoTracking().Where(p => p.BusinessId == id).Select(p => new ProductPrice { ProductId = p.Id, Cost = p.Cost, DesiredProfitPercent = p.DesiredProfitPercent, SalePrice = p.SalePrice, ManualSalePrice = p.ManualSalePrice }).ToListAsync(cancellationToken);
    }
    // Atomic field edits preserve unrelated changes made by another circuit. No schema change.
    public async Task<ProductPrice> ApplyPriceEditAsync(PriceEdit edit)
    {
        var limit = edit.Field switch { PriceField.Cost => 1_000_000_000m, PriceField.DesiredProfitPercent => 10_000m, PriceField.SalePrice => 1_000_000_000_000m, PriceField.Suggested => 0m, _ => throw new ArgumentException("Campo de precio inválido.") };
        if (edit.Value < 0 || edit.Value > limit) throw new ArgumentException("Precio inválido.");
        await EnsureImportedAsync();
        await using var db = await factory.CreateDbContextAsync();
        var id = await BusinessId(db);
        var query = db.Products.Where(p => p.Id == edit.ProductId && p.BusinessId == id);
        var changed = edit.Field switch
        {
            PriceField.Cost => await query.Where(p => p.Cost != edit.Value).ExecuteUpdateAsync(s => s.SetProperty(p => p.Cost, edit.Value).SetProperty(p => p.UpdatedAt, DateTime.UtcNow)),
            PriceField.DesiredProfitPercent => await query.Where(p => p.DesiredProfitPercent != edit.Value).ExecuteUpdateAsync(s => s.SetProperty(p => p.DesiredProfitPercent, edit.Value).SetProperty(p => p.UpdatedAt, DateTime.UtcNow)),
            PriceField.SalePrice => await query.Where(p => p.SalePrice != edit.Value || !p.ManualSalePrice).ExecuteUpdateAsync(s => s.SetProperty(p => p.SalePrice, edit.Value).SetProperty(p => p.ManualSalePrice, true).SetProperty(p => p.UpdatedAt, DateTime.UtcNow)),
            _ => await query.Where(p => p.ManualSalePrice).ExecuteUpdateAsync(s => s.SetProperty(p => p.ManualSalePrice, false).SetProperty(p => p.UpdatedAt, DateTime.UtcNow))
        };
        var actual = await query.AsNoTracking().Select(p => new ProductPrice { ProductId = p.Id, Cost = p.Cost, DesiredProfitPercent = p.DesiredProfitPercent, SalePrice = p.SalePrice, ManualSalePrice = p.ManualSalePrice }).SingleOrDefaultAsync()
            ?? throw new InvalidOperationException("El producto ya no está disponible. Actualizá la lista de Stock.");
        if (changed > 0) changes.Publish(id, "prices");
        return actual;
    }
    public async Task SavePriceAsync(ProductPrice value) {
        ValidatePrice(value); await using var db = await factory.CreateDbContextAsync(); var id = await BusinessId(db);
        var p = await db.Products.SingleAsync(p => p.Id == value.ProductId && p.BusinessId == id);
        p.Cost = value.Cost; p.DesiredProfitPercent = value.DesiredProfitPercent; p.SalePrice = value.SalePrice; p.ManualSalePrice = value.ManualSalePrice; p.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(); changes.Publish(id, "prices");
    }
    public async Task<List<AgendaNote>> LoadNotesAsync() {
        await EnsureImportedAsync(); await using var db = await factory.CreateDbContextAsync(); var id = await BusinessId(db);
        return await db.CalendarEntries.AsNoTracking().Where(n => n.BusinessId == id).Select(n => new AgendaNote(n.Id, n.Date, n.Title, n.Content, n.Time)).ToListAsync();
    }
    public async Task SaveNoteAsync(AgendaNote value) {
        ValidateNote(value); await using var db = await factory.CreateDbContextAsync(); var id = await BusinessId(db);
        var n = await db.CalendarEntries.SingleOrDefaultAsync(n => n.Id == value.Id && n.BusinessId == id);
        if (n is null) { n = new CalendarEntry { Id = value.Id, BusinessId = id }; db.CalendarEntries.Add(n); }
        n.Date = value.Date; n.Title = value.Title; n.Content = value.Content; n.Time = value.Time; n.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(); changes.Publish(id, "notes");
    }
    public async Task DeleteNoteAsync(Guid noteId) {
        await using var db = await factory.CreateDbContextAsync(); var id = await BusinessId(db); await db.CalendarEntries.Where(n => n.Id == noteId && n.BusinessId == id).ExecuteDeleteAsync(); changes.Publish(id, "notes");
    }
    private static void ValidateProduct(StockProduct p) { if (p is null || p.Id == Guid.Empty || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 120 || p.Quantity < 0) throw new ArgumentException("Producto inválido."); }
    private static void ValidatePrice(ProductPrice p) { if (p is null || p.Cost < 0 || p.Cost > 1_000_000_000m || p.DesiredProfitPercent < 0 || p.DesiredProfitPercent > 10_000m || p.SalePrice < 0 || p.SalePrice > 1_000_000_000_000m) throw new ArgumentException("Precio inválido."); }
    private static void ValidateNote(AgendaNote n) { if (n is null || n.Title is null || n.Content is null || (string.IsNullOrWhiteSpace(n.Title) && string.IsNullOrWhiteSpace(n.Content)) || n.Title.Length > 120 || n.Content.Length > 4000 || (n.Time is not null && !System.Text.RegularExpressions.Regex.IsMatch(n.Time, "^([01][0-9]|2[0-3]):[0-5][0-9]$"))) throw new ArgumentException("Nota inválida."); }
}


