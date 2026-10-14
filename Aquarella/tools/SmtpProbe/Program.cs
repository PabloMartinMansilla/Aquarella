using Aquarella.Services.Accounts;
using Aquarella.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

// No host, database, worker, appsettings, or automatic environment default.
return await SmtpProbeRunner.RunAsync(args,
    Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
    () => {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(SmtpEmailSender).Assembly, optional: true, reloadOnChange: false).Build();
        using var lifetime = configuration as IDisposable;
        return configuration.GetSection("Email:Smtp").Get<SmtpEmailOptions>() ?? new();
    },
    options => new SmtpEmailSender(Options.Create(options)), Console.Out, Console.Error);
