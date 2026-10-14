using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Aquarella.Data;
using Microsoft.EntityFrameworkCore;

// Builds no code and never connects to source App_Data. Owns/terminates only its child server.
internal static class Isolated
{
    public static async Task RunAsync(string publishedDirectory)
    {
        var publish = Path.GetFullPath(publishedDirectory);
        if (!File.Exists(Path.Combine(publish, "Aquarella.dll")) || Directory.Exists(Path.Combine(publish, "App_Data")))
            throw new Exception("Pass an isolated publish without private local data.");
        var root = Path.Combine(Path.GetTempPath(), "aquarella-mail-http-" + Guid.NewGuid().ToString("N"));
        var app = Path.Combine(root, "app"); Directory.CreateDirectory(app);
        foreach (var source in Directory.GetFiles(publish, "*", SearchOption.AllDirectories)) {
            var target = Path.Combine(app, Path.GetRelativePath(publish, source)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target);
        }
        var database = Path.Combine(root, "accounts-verification.db");
        await using (var db = new AquarellaDbContext(new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite($"Data Source={database}").Options)) {
            await db.Database.MigrateAsync();
            db.Businesses.Add(new Business { User = new User { Username = "pablo" }, LegacyImported = true }); await db.SaveChangesAsync();
        }
        using var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        var origin = $"https://localhost:{port}";
        Console.WriteLine("Temporary HTTPS fixture: " + root);
        var start = Start(app, Path.Combine(app, "Aquarella.dll"));
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development"; start.Environment["DOTNET_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_URLS"] = origin; start.Environment.Remove("PORT");
        start.Environment["ConnectionStrings__Aquarella"] = $"Data Source={database}";
        start.Environment["Accounts__PublicOrigin"] = origin; start.Environment["Accounts__Mail__ResendSeconds"] = "1";
        start.Environment["DataProtection__KeysPath"] = Path.Combine(root, "keys");
        start.Environment["DevelopmentAccess__Username"] = "pablo";
        start.Environment["Email__Smtp__Enabled"] = "false";
        start.Environment["Accounts__Registration__Enabled"] = "true";
        // Default Development configuration must never load the owner's User Secrets in a verifier.
        var privateConfig = Path.Combine(root, "isolated-user-config"); Directory.CreateDirectory(privateConfig);
        start.Environment["APPDATA"] = privateConfig;
        start.Environment["DOTNET_USER_SECRETS_FALLBACK_DIR"] = privateConfig;
        start.Environment["OpenAI__Enabled"] = "false";
        start.Environment.Remove("OPENAI_API_KEY"); start.Environment.Remove("OpenAI__ApiKey");
        using var server = Process.Start(start)!;
        var output = server.StandardOutput.ReadToEndAsync(); var errors = server.StandardError.ReadToEndAsync();
        try {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var ready = false;
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < deadline) {
                if (server.HasExited) throw new Exception("Isolated server exited: " + await errors);
                try { if ((await client.GetAsync(origin + "/health")).StatusCode == HttpStatusCode.OK) { ready = true; break; } }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { }
                await Task.Delay(100);
            }
            if (!ready) throw new Exception("Isolated HTTPS server did not become ready.");
            await RunChild(typeof(Isolated).Assembly.Location);
            var assemblyDirectory = Path.GetDirectoryName(typeof(Isolated).Assembly.Location)!;
            var toolsRoot = Path.GetFullPath(Path.Combine(assemblyDirectory, "../../../.."));
            var configuration = Directory.GetParent(assemblyDirectory)!.Name;
            await RunChild(Path.Combine(toolsRoot, "DevelopmentAccessVerification", "bin", configuration,
                Path.GetFileName(assemblyDirectory), "DevelopmentAccessVerification.dll"));
            async Task RunChild(string assembly) {
                var childStart = Start(root, assembly);
                childStart.Environment["AQUARELLA_VERIFICATION_URL"] = origin;
                childStart.Environment["AQUARELLA_VERIFICATION_ROOT"] = app;
                childStart.Environment["AQUARELLA_VERIFICATION_DATABASE"] = database;
                using var child = Process.Start(childStart)!;
                var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
                await child.WaitForExitAsync(); Console.Write(await stdout);
                if (child.ExitCode != 0) throw new Exception("HTTP verifier failed: " + await stderr);
            }
        }
        finally {
            if (!server.HasExited) { server.Kill(entireProcessTree: true); await server.WaitForExitAsync(); }
            await File.WriteAllTextAsync(Path.Combine(root, "server.log"), await output + await errors);
        }
        var logs = await output + await errors;
        if (logs.Contains("fail:") || logs.Contains("Unhandled exception") || logs.Contains("token=")) throw new Exception("Unexpected/sensitive isolated server logs; inspect temporary fixture.");
        Console.WriteLine("PASS isolated HTTPS application: startup, HTTP forms, background simulator, Development access, no exception or token in server logs.");
        Console.WriteLine("HTTP artifacts: " + root);
    }
    private static ProcessStartInfo Start(string directory, string assembly) {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(assembly); start.Environment["Logging__EventLog__LogLevel__Default"] = "None"; return start;
    }
}
