using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;

namespace Aquarella.Models;

public sealed record StockIntakeLine(string Name, int Quantity, decimal? UnitCost = null, Guid? ExistingId = null, bool CreateNew = false);
public sealed record StockIntakeItem(Guid Id, string Name, int Quantity);
public sealed record StockIntakeResult(Guid ProductId, string Name, int AddedQuantity, int FinalQuantity, bool Created);
public sealed record StockIntakeOutcome(IReadOnlyList<StockIntakeResult> Lines, bool AlreadyApplied);

public static class StockIntakePlanner
{
    // Keep accents, units, digits, slash, plus and decimal precision: no fuzzy matching.
    public static string Normalize(string name)
    {
        var value = name.Normalize(NormalizationForm.FormKC).ToUpperInvariant().Trim();
        value = Regex.Replace(value, @"(?<=\d),(?=\d)", ".");
        value = Regex.Replace(value, @"[-–—,;:!?]", " ");
        value = Regex.Replace(value, @"(?<!\d)\.|\.(?!\d)", " ");
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    public static IReadOnlyList<StockIntakeResult> Plan(IEnumerable<StockIntakeItem> current, IReadOnlyList<StockIntakeLine> lines)
    {
        if (lines.Count is < 1 or > 200) throw new ValidationException("Revisá entre 1 y 200 productos por carga.");
        var items = current.ToList();
        var result = new List<StockIntakeResult>();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var label = $"Producto {index + 1}";
            if (string.IsNullOrWhiteSpace(line.Name) || line.Name.Trim().Length > 120)
                throw new ValidationException($"{label}: ingresá un nombre de hasta 120 caracteres.");
            if (line.Quantity < 0) throw new ValidationException($"{label}: la cantidad no puede ser negativa.");
            if (line.UnitCost is < 0 or > 1_000_000_000m) throw new ValidationException($"{label}: el costo debe estar entre 0 y 1.000.000.000.");
            StockIntakeItem? match = null;
            if (line.ExistingId is Guid id)
                match = items.SingleOrDefault(p => p.Id == id) ?? throw new ValidationException($"{label}: el producto asociado ya no está disponible.");
            else if (!line.CreateNew && Normalize(line.Name).Length > 0)
            {
                var matches = items.Where(p => Normalize(p.Name) == Normalize(line.Name)).ToList();
                if (matches.Count > 1) throw new ValidationException($"{label}: hay más de una coincidencia. Elegí un producto existente o crear uno nuevo.");
                match = matches.SingleOrDefault();
            }
            var final = (long)(match?.Quantity ?? 0) + line.Quantity;
            if (final > int.MaxValue) throw new ValidationException($"{label}: la cantidad final supera 2.147.483.647.");
            var item = new StockIntakeItem(match?.Id ?? Guid.NewGuid(), match?.Name ?? line.Name.Trim(), (int)final);
            if (match is null) items.Add(item); else items[items.IndexOf(match)] = item;
            result.Add(new(item.Id, item.Name, line.Quantity, item.Quantity, match is null));
        }
        return result;
    }
}
