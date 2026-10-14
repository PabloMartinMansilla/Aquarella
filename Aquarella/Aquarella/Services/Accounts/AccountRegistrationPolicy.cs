namespace Aquarella.Services.Accounts;

// Production is private by default. Reopening registration still requires a working email sender.
public sealed class AccountRegistrationPolicy(IConfiguration configuration, IHostEnvironment environment)
{
    public bool Enabled { get; } = configuration.GetValue<bool?>("Accounts:Registration:Enabled")
        ?? environment.IsDevelopment();
    public const string ClosedMessage = "La beta es privada. El registro de nuevas cuentas no está habilitado.";
}
