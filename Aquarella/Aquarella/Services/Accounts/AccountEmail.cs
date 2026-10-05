using System.Text.Json;
namespace Aquarella.Services.Accounts;
public interface IAccountEmail { bool Available { get; } Task SendAsync(string recipient, string purpose, string link); }
public sealed class UnavailableAccountEmail : IAccountEmail
{
    public bool Available => false;
    public Task SendAsync(string recipient, string purpose, string link) =>
        throw new InvalidOperationException("El proveedor de correo no está configurado.");
}
// Private local outbox, never mapped as static files. No provider or outbound email request.
public sealed class DevelopmentAccountEmail(IWebHostEnvironment environment) : IAccountEmail
{
    public bool Available => environment.IsDevelopment();
    public async Task SendAsync(string recipient, string purpose, string link) {
        if (!environment.IsDevelopment()) throw new InvalidOperationException("El proveedor de email todavía no está configurado.");
        var folder = Path.Combine(environment.ContentRootPath, "App_Data", "development-emails"); Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json"), JsonSerializer.Serialize(new { Recipient = recipient, Purpose = purpose, Link = link, CreatedAt = DateTime.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
