using System.ComponentModel.DataAnnotations;

namespace Aquarella.Models;

public enum AiResponseDetail { Brief, Balanced, Detailed }
public enum AiResponseTone { Direct, Friendly, Professional }
public enum AiProactivityLevel { Low, Balanced, High }

// Preferences only. They grant no authorization and are not consumed by any AI service.
// Memory, history, web search and action policies belong to their future implementations.
public sealed class AiSettings
{
    [EnumDataType(typeof(AiResponseDetail), ErrorMessage = "Elegí un estilo de respuesta válido.")]
    public AiResponseDetail AiResponseDetail { get; set; } = AiResponseDetail.Balanced;
    [EnumDataType(typeof(AiResponseTone), ErrorMessage = "Elegí un tono válido.")]
    public AiResponseTone AiResponseTone { get; set; } = AiResponseTone.Friendly;
    [StringLength(2000, ErrorMessage = "Las instrucciones admiten hasta 2.000 caracteres.")]
    public string? AiCustomInstructions { get; set; } = "";
    public bool AiCanAccessStock { get; set; }
    public bool AiCanAccessPricing { get; set; }
    public bool AiCanAccessAgenda { get; set; }
    public bool AiCanAccessAlerts { get; set; }
    public bool AiProactiveSuggestionsEnabled { get; set; }
    [EnumDataType(typeof(AiProactivityLevel), ErrorMessage = "Elegí un nivel de sugerencias válido.")]
    public AiProactivityLevel AiProactivityLevel { get; set; } = AiProactivityLevel.Low;
    public bool AiExplainRecommendations { get; set; } = true;
}
