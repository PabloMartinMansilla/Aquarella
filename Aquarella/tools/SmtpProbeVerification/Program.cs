using Aquarella.Services.Accounts;
using Aquarella.Tools;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
SmtpEmailOptions Config() => new() { Enabled = true, Host = "fixture.invalid", Username = "fixture@example.test", Password = "FICTIONAL-PASSWORD", FromAddress = "fixture@example.test" };
foreach (var development in new[] { true, false }) foreach (var enabled in new[] { true, false }) {
    var options = Config(); options.Enabled = enabled;
    var services = new ServiceCollection(); services.AddSingleton<IWebHostEnvironment>(new FixtureEnvironment());
    services.AddSingleton(Options.Create(options)); AccountEmailTransport.Register(services, development, enabled);
    using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
    var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
    Check(development && !enabled ? sender is DevelopmentEmailSender : sender is SmtpEmailSender, "Transport selection");
    Check(sender.Available == (development || enabled), "Production disabled remains unavailable");
}
var loadCount = 0; var createCount = 0;
var mock = new FixtureSender(); var configured = Config();
var output = new StringWriter(); var error = new StringWriter();
Task<int> Run(string[] arguments, string? env = "Development", string? asp = null) => SmtpProbeRunner.RunAsync(arguments, env, asp,
    () => { loadCount++; return configured; }, _ => { createCount++; return mock; }, output, error);
Check(await Run([]) == 0 && await Run(["--help"], "Production") == 0, "Help does not load secrets");
foreach (var pair in new (string?, string?)[] { (null, null), ("Production", null), ("Staging", null), ("development", null), ("Development", "Production") })
    Check(await Run(["--send", "--to", "owned@example.test"], pair.Item1, pair.Item2) == 1, "Fail closed environment");
foreach (var arguments in new[] {
    new[] { "--to", "owned@example.test" }, new[] { "--send" }, new[] { "--send", "--to", "bad" },
    new[] { "--send", "--to", "one@example.test,two@example.test" }, new[] { "--send", "--to", "one@example.test;two@example.test" },
    new[] { "--send", "--to", "Owned <owned@example.test>" }, new[] { "--send", "--to", "owned@example.test\r\nBcc: other@example.test" },
    new[] { "--send", "--to", "owned@example.test", "--send" }, new[] { "--send", "--to", "owned@example.test", "--password", "untrusted-input" } })
    Check(await Run(arguments) == 1, "Explicit single valid recipient required");
Check(loadCount == 0 && createCount == 0 && mock.Calls == 0, "All refusals happen before secrets/transport");
configured.Enabled = false;
Check(await Run(["--send", "--to", "owned@example.test"]) == 1 && mock.Calls == 0 && createCount == 0, "Disabled means no send");
configured = Config(); configured.Security = "Ssl";
Check(await Run(["--send", "--to", "owned@example.test"]) == 1 && createCount == 0, "Invalid TLS refused");
configured = Config(); mock.Available = false;
Check(await Run(["--send", "--to", "owned@example.test"]) == 1 && mock.Calls == 0, "Unavailable sender refused");
mock.Available = true;
Check(await Run(["--send", "--to", "owned@example.test"], "Development", "Development") == 0 && mock.Calls == 1, "One effective send");
Check(mock.Message is { Recipient: "owned@example.test", Purpose: "smtp-test", Link.Length: 0 } && !mock.Message.Body.Contains("http"), "Fixed test content without user data or links");
mock.Fail = true;
Check(await Run(["--send", "--to", "owned@example.test"]) == 1 && mock.Calls == 2, "Failure never retries");
Check(!(output.ToString() + error).Contains("FICTIONAL-PASSWORD") && !(output.ToString() + error).Contains("SENSITIVE-PROVIDER") && !error.ToString().Contains("untrusted-input"), "Sensitive output suppressed");
Console.WriteLine($"PASS SMTP probe: {checks} checks; transport matrix, environment/refusal before secrets, single recipient/send, failure without retry and safe output. No secrets read, network, host or SQLite.");

sealed class FixtureSender : IEmailSender {
    public bool Available { get; set; } = true;
    public int Calls { get; private set; }
    public bool Fail { get; set; }
    public EmailMessage? Message { get; private set; }
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) {
        Calls++; Message = message;
        return Fail ? Task.FromException(new IOException("SENSITIVE-PROVIDER FICTIONAL-PASSWORD")) : Task.CompletedTask;
    }
}
sealed class FixtureEnvironment : IWebHostEnvironment {
    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "Fixture";
    public string ContentRootPath { get; set; } = "unused";
    public string WebRootPath { get; set; } = "unused";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
