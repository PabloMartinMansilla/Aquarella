using Aquarella.Models;
using System.Reflection;

namespace Aquarella.Services;

// Only the editable string fields of this profile; no resource IDs or owner selection.
internal static class BusinessProfileChanges
{
    private static readonly PropertyInfo[] Fields = typeof(BusinessProfile).GetProperties()
        .Where(p => p.CanWrite && p.PropertyType == typeof(string)).ToArray();

    public static BusinessProfile Merge(BusinessProfile original, BusinessProfile edited, BusinessProfile current)
    {
        var result = current.Copy();
        foreach (var field in Fields)
        {
            var before = (string?)field.GetValue(original);
            var after = (string?)field.GetValue(edited);
            // HEX casing and empty optional representations are not edits.
            var comparison = field.Name.EndsWith("Color", StringComparison.Ordinal)
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(before ?? "", after ?? "", comparison)) field.SetValue(result, after);
        }
        return result;
    }
}
