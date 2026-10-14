using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
namespace Aquarella.Services.Accounts;

public sealed record EmailMessage(string Recipient, string Subject, string Body, string Purpose = "", string Link = "");
public interface IEmailSender
{
    bool Available { get; }
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
public interface IAccountEmail { bool Available { get; } Task SendAsync(string recipient, string purpose, string link); }
public static class AccountEmailTransport
{
    public static void Register(IServiceCollection services, bool development, bool smtpEnabled)
    {
        if (development && !smtpEnabled) services.AddScoped<IEmailSender, DevelopmentEmailSender>();
        else services.AddScoped<IEmailSender, SmtpEmailSender>();
    }
}
// Account templates are separate from transport; other features may use IEmailSender.
public sealed class AccountEmail(IEmailSender sender) : IAccountEmail
{
    public bool Available => sender.Available;
    public Task SendAsync(string recipient, string purpose, string link) => sender.SendAsync(Content(recipient, purpose, link));
    public static EmailMessage Content(string recipient, string purpose, string link) => purpose switch {
        "verify" => new(recipient, "Verificá tu correo en Aquarella", $"Confirmá tu cuenta abriendo este enlace y presionando Verificar correo:\n{link}\n\nEl enlace es temporal y de un solo uso. Si no solicitaste esta cuenta, ignorá este mensaje.", purpose, link),
        "reset" => new(recipient, "Restablecé tu contraseña de Aquarella", $"Para elegir una nueva contraseña, abrí este enlace:\n{link}\n\nEl enlace es temporal y de un solo uso. Si no lo solicitaste, ignorá este mensaje: tu contraseña no cambió.", purpose, link),
        "password-changed" => new(recipient, "Tu contraseña de Aquarella fue modificada", "Se modificó la contraseña de tu cuenta de Aquarella y se cerraron las sesiones anteriores. Si no realizaste este cambio, solicitá una recuperación desde la aplicación y contactá al responsable de Aquarella.", purpose),
        _ => throw new ArgumentException("Propósito de correo no válido.", nameof(purpose))
    };
}
public sealed class UnavailableAccountEmail : IAccountEmail
{
    public bool Available => false;
    public Task SendAsync(string recipient, string purpose, string link) => throw new InvalidOperationException("El proveedor de correo no está configurado.");
}
// Private local outbox, never mapped/published; cannot run in Production.
public sealed class DevelopmentEmailSender(IWebHostEnvironment environment) : IEmailSender
{
    public bool Available => environment.IsDevelopment();
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!Available) throw new InvalidOperationException("Correo simulado disponible únicamente en Development.");
        var folder = Path.Combine(environment.ContentRootPath, "App_Data", "development-emails");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json"),
            JsonSerializer.Serialize(new { message.Recipient, message.Purpose, message.Link, message.Subject, message.Body, CreatedAt = DateTime.UtcNow },
                new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }
}
public sealed class DevelopmentAccountEmail(IWebHostEnvironment environment) : IAccountEmail
{
    private readonly AccountEmail adapter = new(new DevelopmentEmailSender(environment));
    public bool Available => adapter.Available;
    public Task SendAsync(string recipient, string purpose, string link) => adapter.SendAsync(recipient, purpose, link);
}
// Required STARTTLS (587), not implicit TLS (465). Default certificate validation.
public sealed class SmtpEmailSender(IOptions<SmtpEmailOptions> configured) : IEmailSender
{
    public bool Available => configured.Value.IsConfigured;
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!Available) throw new InvalidOperationException("SMTP no está configurado de forma segura.");
        var options = configured.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try {
            using var smtp = new SmtpClient(options.Host, options.Port) {
                EnableSsl = true, UseDefaultCredentials = false,
                Credentials = new NetworkCredential(options.Username, options.Password), DeliveryMethod = SmtpDeliveryMethod.Network
            };
            using var mail = new MailMessage {
                From = new MailAddress(options.FromAddress, options.FromName), Subject = message.Subject,
                Body = message.Body, IsBodyHtml = false, BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(new MailAddress(message.Recipient));
            await smtp.SendMailAsync(mail, timeout.Token);
        }
        catch (Exception e) when (e is SmtpException or OperationCanceledException or FormatException or ArgumentException) {
            // Provider replies and inner exceptions may contain sensitive data: do not propagate them.
            throw new IOException("No se pudo entregar el correo. El proveedor rechazó el envío o no respondió.");
        }
    }
}
