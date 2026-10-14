using System.Net.Mail;
using Aquarella.Services.Accounts;

namespace Aquarella.Tools;

public static class SmtpProbeRunner
{
    public static async Task<int> RunAsync(string[] args, string? dotnetEnvironment, string? aspnetEnvironment,
        Func<SmtpEmailOptions> loadOptions, Func<SmtpEmailOptions, IEmailSender> createSender,
        TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args is ["--help"]) {
            await output.WriteLineAsync("Solo Development: --send --to <correo-de-prueba>. Envía como máximo un correo; lee Email:Smtp de User Secrets únicamente después de validar la solicitud.");
            return 0;
        }
        // Missing or conflicting environment is refused before loading any secret.
        if (dotnetEnvironment != "Development" || aspnetEnvironment is not (null or "Development"))
            return await Refuse("La prueba exige DOTNET_ENVIRONMENT=Development y ningún entorno ASPNETCORE incompatible.");
        if (args is not ["--send", "--to", var recipient] || recipient.Length > 254 ||
            recipient.Any(char.IsWhiteSpace) || recipient.IndexOfAny(['\r', '\n', ',', ';']) >= 0 ||
            !MailAddress.TryCreate(recipient, out var address) || address.Address != recipient || !recipient.Contains('@'))
            return await Refuse("Indicá exactamente --send --to <un-correo-de-prueba>, sin opciones adicionales.");
        try {
            var options = loadOptions();
            if (!options.IsConfigured) return await Refuse("SMTP está deshabilitado o incompleto. Revisá Email:Smtp en tus User Secrets privados.");
            var sender = createSender(options);
            if (!sender.Available) return await Refuse("El transporte SMTP no está disponible.");
            await output.WriteLineAsync("Enviando un único mensaje de prueba SMTP; sin datos de usuarios ni enlaces de cuenta.");
            // One invocation, one recipient, no queue and no retry, even after an ambiguous failure.
            await sender.SendAsync(new EmailMessage(recipient, "Prueba SMTP de Aquarella",
                "Este es un mensaje de prueba del transporte SMTP de Aquarella. No contiene datos de usuarios, tokens ni enlaces de verificación o recuperación.", "smtp-test"));
            await output.WriteLineAsync("El servidor SMTP aceptó el mensaje. Comprobá la recepción y la carpeta de spam; esto no valida los flujos de cuentas.");
            return 0;
        }
        catch (Exception) {
            // Do not disclose provider responses, configuration values, recipients, or stack traces.
            return await Refuse("La prueba no pudo completarse. No se reintentó; el proveedor podría haber aceptado el mensaje antes del fallo. Revisá la bandeja antes de ejecutar otra vez.");
        }
        async Task<int> Refuse(string message) { await error.WriteLineAsync(message); return 1; }
    }
}
