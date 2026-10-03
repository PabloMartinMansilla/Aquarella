using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Aquarella.Services;

public sealed class AquarellaChatService(
    HttpClient httpClient,
    IOptions<OpenAIOptions> options)
{
    public bool UsesOpenAI => options.Value.Enabled
        && !string.IsNullOrWhiteSpace(options.Value.ApiKey)
        && !string.IsNullOrWhiteSpace(options.Value.Model);

    public async Task<string> ReplyAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!UsesOpenAI)
        {
            return "Recibí tu mensaje. Esta es una respuesta simulada de Aquarella. ¿En qué más puedo ayudarte?";
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey!.Trim());
            request.Content = JsonContent.Create(new
            {
                model = options.Value.Model,
                instructions = "Sos Aquarella, un asistente amable. Respondé en español de forma clara y breve.",
                input = message,
                store = false
            });

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return "No pude obtener una respuesta de OpenAI. Intentá nuevamente más tarde.";
            }

            using var body = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var texts = new List<string>();
            if (body.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in output.EnumerateArray())
                {
                    if (!item.TryGetProperty("type", out var itemType) || itemType.GetString() != "message"
                        || !item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var part in content.EnumerateArray())
                    {
                        if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text"
                            && part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        {
                            texts.Add(text.GetString()!);
                        }
                    }
                }
            }

            var reply = string.Join("\n", texts);
            return string.IsNullOrWhiteSpace(reply) ? "OpenAI no devolvió una respuesta de texto. Intentá nuevamente." : reply;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            // No mostrar ni registrar la clave, el cuerpo de la solicitud ni errores remotos.
            return "No pude obtener una respuesta de OpenAI. Intentá nuevamente más tarde.";
        }
    }
}
