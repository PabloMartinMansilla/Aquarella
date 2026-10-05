using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aquarella.Models;

namespace Aquarella.Services;

public static class AiSettingsCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public static AiSettings Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        var settings = JsonSerializer.Deserialize<AiSettings>(json, Options)
            ?? throw new JsonException("La configuración de IA guardada no es válida.");
        Validate(settings);
        return settings;
    }
    public static string Write(AiSettings settings)
    {
        Validate(settings);
        return JsonSerializer.Serialize(settings, Options);
    }
    private static void Validate(AiSettings settings) =>
        Validator.ValidateObject(settings, new ValidationContext(settings), validateAllProperties: true);
}
