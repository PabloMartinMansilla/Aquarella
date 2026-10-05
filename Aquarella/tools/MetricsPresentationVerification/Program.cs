using Aquarella.Components;
using Aquarella.Data;
using Aquarella.Models;
using Aquarella.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;
using System.Text.Json;

// Isolated fixtures only: never connects to application configuration or its local database.
var culture = CultureInfo.GetCultureInfo("es-AR");
Product[] Fixture(string mode) => mode switch {
    "empty" => [],
    "missing" => [new() { Name = "Producto sin precios", Quantity = 4 }],
    "few" => [new() { Name = "Producto único", Quantity = 2, Cost = 12.35m, DesiredProfitPercent = 30 }],
    "tiny" => [new() { Name = "Porcentaje pequeño", Quantity = 1, Cost = 100, SalePrice = 100.01m, ManualSalePrice = true }],
    "zero-stock" => [new() { Name = "Agotado", Quantity = 0, Cost = 100, SalePrice = 130, ManualSalePrice = true }],
    "extreme" => [new() { Name = new string('W', 120), Quantity = int.MaxValue, Cost = 1000000000.99m, SalePrice = 1234567890.12m, ManualSalePrice = true }, new() { Name = "Costo mínimo", Quantity = 1, Cost = .01m, SalePrice = 1000000, ManualSalePrice = true }, new() { Name = "Pérdida total", Quantity = 1, Cost = 1, SalePrice = 0, ManualSalePrice = true }],
    _ => Enumerable.Range(0, 40).Select(i => new Product { Name = i switch { 0 => "Café de especialidad", 1 => "Aceite de oliva", 2 => "Chocolate amargo", 3 => "Té en hebras", _ => $"Producto {i + 1:00}" }, Quantity = i % 6 == 0 ? 0 : 2 + i, Cost = i % 9 == 0 ? 0 : 125.50m + i * 10, SalePrice = i % 7 == 0 ? 90 : 140m + i * 25, ManualSalePrice = i % 9 != 0 }).ToArray()
};
BusinessMetrics Calculate(Product[] products) => MetricsCalculator.Calculate(products.Select(p => new NoticeProduct(new(p.Id, p.Name, p.Quantity), new() { Cost = p.Cost, SalePrice = p.SalePrice, ManualSalePrice = p.ManualSalePrice, DesiredProfitPercent = p.DesiredProfitPercent })).ToArray());
if (args.Length == 2) {
    var path = Path.GetFullPath(args[0]);
    if (!path.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(path).StartsWith("aquarella-metrics-fixture-"))
        throw new InvalidOperationException("Fixture database must be a dedicated file in the temporary directory.");
    await using var db = new AquarellaDbContext(new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite($"Data Source={path}").Options);
    await db.Database.MigrateAsync();
    var user = await db.Users.Include(u => u.Business).SingleOrDefaultAsync(u => u.Username == "metrics-visual-fixture");
    if (user is null) { user = new() { Username = "metrics-visual-fixture", HasCompletedOnboarding = true }; db.Businesses.Add(new() { User = user, LegacyImported = true }); await db.SaveChangesAsync(); }
    await db.Products.Where(p => p.BusinessId == user.Business!.Id).ExecuteDeleteAsync();
    var products = Fixture(args[1]);
    foreach (var p in products) p.BusinessId = user.Business!.Id;
    db.Products.AddRange(products); await db.SaveChangesAsync();
    Console.WriteLine(JsonSerializer.Serialize(Calculate(products)));
    return;
}
var services = new ServiceCollection().AddLogging().BuildServiceProvider();
await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
async Task<string> Render<T>(Dictionary<string, object?> parameters) where T : IComponent => await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters))).ToHtmlString());
foreach (var mode in new[] { "empty", "missing", "few", "tiny", "zero-stock", "many", "extreme" }) {
    var m = Calculate(Fixture(mode));
    foreach (var (value, format) in new (decimal?, string)[] { (m.ProductCount, "count"), (m.Units, "count"), (m.InventoryCost, "money"), (m.InventorySaleValue, "money"), (m.WeightedProfitPercent, "percent") }) {
        var expected = value is decimal number ? (format == "money" ? "$" : "") + number.ToString(format == "count" ? "N0" : "N2", culture) + (format == "percent" ? "%" : "") : "Sin datos";
        var html = WebUtility.HtmlDecode(await Render<MetricKpi>(new() { ["Title"] = mode, ["Value"] = value, ["Format"] = format }));
        Check(html.Contains($"data-final=\"{expected}\"") && html.Contains($"aria-label=\"{expected}\""), $"{mode} {format}: exact and accessible final value");
    }
    var scale = m.HighestProfit.Concat(m.LowestProfit).Select(p => Math.Abs(p.ProfitPercent)).DefaultIfEmpty(0).Max();
    var bars = WebUtility.HtmlDecode(await Render<MetricProductList>(new() { ["Products"] = m.LowestProfit, ["Scale"] = scale }));
    foreach (var p in m.LowestProfit) {
        Check(bars.Contains(p.ProfitPercent.ToString("N2", culture) + "%") && bars.Contains(p.ProfitPercent < 0 ? "negative" : "positive"), $"{mode}: actual signed percentages");
        var width = (Math.Abs(p.ProfitPercent) / (scale > 0 ? scale : 1) * 50).ToString("0.################", CultureInfo.InvariantCulture);
        Check(bars.Contains($"width:{width}%"), $"{mode}: signed bar uses common exact scale");
    }
    Check(m.ProductsInStock + m.OutOfStock == m.ProductCount, $"{mode}: stock partition");
}
Console.WriteLine($"{checks} comprobaciones de presentación correctas: valores exactos, formatos, accesibilidad, negativos y casos límite.");
