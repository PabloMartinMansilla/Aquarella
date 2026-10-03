namespace Aquarella.Services;

public sealed class OpenAIOptions
{
    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }
    public string Model { get; set; } = string.Empty;
}
