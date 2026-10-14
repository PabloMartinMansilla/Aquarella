using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Aquarella.Services.Accounts;
public sealed class AccountMailOptions
{
    public int VerificationHours { get; set; } = 24;
    public int RecoveryMinutes { get; set; } = 30;
    public int ResendSeconds { get; set; } = 60;
    public int SendsPerHour { get; set; } = 5;
    public int NotificationRetryMinutes { get; set; } = 5;
    public void Validate()
    {
        if (VerificationHours is < 1 or > 168 || RecoveryMinutes is < 1 or > 120 || ResendSeconds is < 1 or > 3600 ||
            SendsPerHour is < 1 or > 10 || NotificationRetryMinutes is < 1 or > 60)
            throw new OptionsValidationException(nameof(AccountMailOptions), typeof(AccountMailOptions), ["Configuración de tiempos de correo inválida."]);
    }
    public TimeSpan Lifetime(string purpose) => purpose switch {
        "verify" => TimeSpan.FromHours(VerificationHours), "reset" => TimeSpan.FromMinutes(RecoveryMinutes),
        "legacy" => TimeSpan.FromHours(24), _ => throw new ArgumentException("Propósito no válido.", nameof(purpose))
    };
}
public sealed class SmtpEmailOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Security { get; set; } = "StartTls";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Aquarella";
    public int TimeoutSeconds { get; set; } = 20;
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Host) && !Host.Any(char.IsWhiteSpace) &&
        Port is > 0 and <= 65535 && Port != 465 && Security == "StartTls" &&
        !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password) &&
        new EmailAddressAttribute().IsValid(FromAddress) && !FromAddress.Any(c => c is '\r' or '\n') &&
        !FromName.Any(c => c is '\r' or '\n') && TimeoutSeconds is >= 1 and <= 30;
}
public static class AccountMailOrigin
{
    public static string Validate(string? configured, bool development)
    {
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || (!development && (uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("Configurá Accounts:PublicOrigin con un origen HTTPS válido para este entorno.");
        return uri.GetLeftPart(UriPartial.Authority);
    }
    public static string FromRequest(IConfiguration configuration, IWebHostEnvironment environment, HttpRequest request) =>
        Validate(configuration["Accounts:PublicOrigin"] ??
            (environment.IsDevelopment() && request.Host.Host == "localhost" ? $"{request.Scheme}://{request.Host}" : null), environment.IsDevelopment());
}
