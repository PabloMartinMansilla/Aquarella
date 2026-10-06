using Aquarella.Models;
using Aquarella.Services;
using Aquarella.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : "Aquarella/Aquarella");
var fixtures = Path.Combine(Path.GetTempPath(), "aquarella-product-visual-preview");
Directory.CreateDirectory(fixtures);
var names = new[] { "Cuaderno A4", "Lápiz negro", "Lapicera azul", "Resma A4", "Tijera escolar", "Auto de juguete", "Regalo", "Impresora", "producto desconocido" };
var expected = new[] { "notebook", "pencil", "pen", "paper", "scissors", "car", "gift", "printer", "generic" };
var id = Guid.Parse("de0e071e-dc62-4cc3-a07d-cc83b93d4fb7");
for (int i = 0; i < names.Length; i++) Check(ProductVisualResolver.Resolve(id, names[i]).Type == expected[i], names[i]);
Check(ProductVisualResolver.Resolve(id, "Cuaderno", "Tijera", "Lápiz").Type == "scissors", "Category priority");
Check(ProductVisualResolver.Resolve(id, "Cuaderno", "Librería", "Lápiz").Type == "pencil", "Subtype after unclassified category");
Check(ProductVisualResolver.Resolve(id, null).Type == "generic", "Missing name fallback");
Check(ProductVisualResolver.Resolve(id, "automático pegamentoso").Type == "generic", "Whole words, no substring false positive");
var assets = Path.Combine(root, "wwwroot", "images", "products");
foreach (var type in ProductVisualResolver.Types)
    for (int variant = 0; variant < ProductVisualResolver.VariantCount; variant++)
    {
        var bytes = File.ReadAllBytes(Path.Combine(assets, $"{type}-{variant}.webp"));
        Check(System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP", $"WebP {type}-{variant}");
        Check(bytes.Length < 8000, "Small asset");
    }
var services = new ServiceCollection().AddLogging().BuildServiceProvider();
await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
var rows = new List<string>();
var priceRows = new List<string>();
var variants = new HashSet<int>();
for (int i = 0; i < 100; i++)
{
    var product = new StockProduct(new Guid(i, 0, 0, new byte[8]), names[i % names.Length], i + 1);
    var stockVisual = ProductVisualResolver.Resolve(product);
    var priceVisual = ProductVisualResolver.Resolve(product.Id, product.Name);
    Check(stockVisual == priceVisual && stockVisual == ProductVisualResolver.Resolve(product), "Same appearance across views/repeated renders");
    variants.Add(stockVisual.Variant);
    Check(ProductVisualResolver.Resolve(product.Id, "renamed").Variant == stockVisual.Variant, "Colour survives name changes");
    var html = await renderer.Dispatcher.InvokeAsync(async () => {
        var output = await renderer.RenderComponentAsync<ProductThumbnail>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Product"] = product }));
        return output.ToHtmlString();
    });
    Check(html.Contains("alt=\"\"") && html.Contains("aria-hidden=\"true\"") && html.Contains("loading=\"lazy\"") && !html.Contains("canvas"), "Decorative, lazy and no renderer");
    priceRows.Add($"<tr><th scope='row'><div class='price-product'>{html}<span>{product.Name}</span></div></th><td><input value='1.000'></td><td><input value='30'></td><td>1.300,00</td><td><input value='1.300'></td><td>300,00</td></tr>");
    rows.Add($"<article class='product-row'><div class='product-info'>{html}<div class='product-description'><h2>{product.Name}</h2><p>Cantidad actual: <strong>{product.Quantity}</strong></p></div></div><div>Ajuste de cantidad <button>−</button> <input value='0' size='2'> <button>+</button></div><div>Editar · Eliminar</div></article>");
}
Check(variants.Count == 6, "All six deterministic variants");
foreach (var page in new[] { "Stock", "Precios" }) Check(File.ReadAllText(Path.Combine(root, "Components", "Pages", page + ".razor")).Contains("<ProductThumbnail Product=\"product\" />"), "Shared component: " + page);
var css = File.ReadAllText(Path.Combine(root, "Components", "ProductThumbnail.razor.css"));
Check(css.Contains("prefers-reduced-motion") && !css.Contains("infinite") && !css.Contains("will-change"), "Reduced motion and no persistent animation/layers");
Directory.CreateDirectory(Path.Combine(fixtures, "images", "products"));
foreach (var path in Directory.GetFiles(assets)) File.Copy(path, Path.Combine(fixtures, "images", "products", Path.GetFileName(path)), true);
var styles = File.ReadAllText(Path.Combine(root, "Components", "Pages", "Stock.razor.css")) + css;
var htmlFixture = "<!doctype html><html lang='es'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Aquarella — 100 miniaturas de prueba</title><style>:root{--border:#dce2ee;--surface:#fff;--surface-text:#243249;--surface-muted:#637084;--secondary:#598cbe;--on-secondary:#fff;--muted:#637084}body{font:15px system-ui;margin:24px;background:#f7f9fd}h1{margin-bottom:20px}" + styles + "</style><h1>Stock — 100 productos ficticios</h1><section class='product-list'>" + string.Join("", rows) + "</section><script>document.querySelectorAll('.product-thumbnail img').forEach(img=>{img.onload=()=>{img.classList.add('is-loaded');img.parentElement.classList.add('is-ready')};img.onerror=()=>{img.hidden=true;img.parentElement.classList.remove('is-ready')};if(img.complete&&img.naturalWidth)img.onload()});</script></html>";
File.WriteAllText(Path.Combine(fixtures, "index.html"), htmlFixture);
var priceStyles = File.ReadAllText(Path.Combine(root, "Components", "Pages", "Precios.razor.css")) + css;
var priceFixture = htmlFixture.Replace(styles, priceStyles).Replace("Stock — 100 productos ficticios", "Precios — 100 productos ficticios");
var sectionStart = priceFixture.IndexOf("<section class='product-list'>", StringComparison.Ordinal);
var sectionEnd = priceFixture.IndexOf("</section>", sectionStart, StringComparison.Ordinal) + "</section>".Length;
priceFixture = priceFixture[..sectionStart] + "<div class='table-container'><table><thead><tr><th>Producto</th><th>Compra</th><th>% deseado</th><th>Sugerido</th><th>Venta</th><th>Ganancia</th></tr></thead><tbody>" + string.Join("", priceRows) + "</tbody></table></div>" + priceFixture[sectionEnd..];
File.WriteAllText(Path.Combine(fixtures, "precios.html"), priceFixture);
Check(css.Contains("data:image/webp;base64,"), "Network-independent generic fallback");
Console.WriteLine($"PASS: classification, precedence, stable color, 90 assets, decorative markup and 100 shared resolutions. Preview: {fixtures}");
static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); }
