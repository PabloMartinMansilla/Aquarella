using System.Globalization;
using System.Text;
using Aquarella.Models;

namespace Aquarella.Services;

public readonly record struct ProductVisual(string Type, int Variant)
{
    public string Asset => $"images/products/{Type}-{Variant}.webp";
}

// Presentation only: no database, network, randomized hash or business state.
public static class ProductVisualResolver
{
    public const int VariantCount = 6;
    public static readonly string[] Types = ["notebook", "pencil", "pen", "ruler", "eraser", "scissors", "glue", "folder", "paper", "ball", "car", "toy", "gift", "printer", "generic"];
    private static readonly (string Type, string[] Words)[] Rules = [
        ("notebook", ["cuaderno", "libro", "anotador", "agenda"]),
        ("pencil", ["lapiz", "lapices", "crayon", "crayones"]),
        ("pen", ["lapicera", "boligrafo", "marcador", "fibron", "fibras", "resaltador"]),
        ("ruler", ["regla", "escuadra", "transportador"]),
        ("eraser", ["goma", "borrador"]),
        ("scissors", ["tijera", "tijeras"]),
        ("glue", ["pegamento", "adhesivo", "plasticola", "cola"]),
        ("folder", ["carpeta", "bibliorato", "sobre"]),
        ("paper", ["resma", "papel", "hoja", "hojas", "documento", "fotocopia", "impresion"]),
        ("ball", ["pelota", "balon"]),
        ("car", ["auto", "autito", "coche", "camion", "hot wheels"]),
        ("toy", ["juguete", "juguetes", "muneco", "muneca", "bloque", "cubo", "peluche"]),
        ("gift", ["regalo", "regalos", "juego", "taza", "bolsa", "decoracion"]),
        ("printer", ["impresora", "cartucho", "toner", "tinta"])
    ];

    public static ProductVisual Resolve(StockProduct product) => Resolve(product.Id, product.Name);
    public static ProductVisual Resolve(Guid id, string? name, string? category = null, string? subtype = null)
    {
        var type = Classify(category) ?? Classify(subtype) ?? Classify(name) ?? "generic";
        // FNV-1a over canonical GUID characters stays stable across processes/platforms.
        uint hash = 2166136261;
        foreach (var character in id.ToString("N")) hash = unchecked((hash ^ character) * 16777619);
        return new(type, (int)(hash % VariantCount));
    }

    private static string? Classify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new StringBuilder(" ");
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            normalized.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }
        normalized.Append(' ');
        var text = normalized.ToString();
        foreach (var rule in Rules)
            foreach (var word in rule.Words)
                if (text.Contains($" {word} ", StringComparison.Ordinal)) return rule.Type;
        return null;
    }
}
