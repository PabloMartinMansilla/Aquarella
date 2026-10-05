using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Aquarella.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

if (args.Length != 1) throw new ArgumentException("Pass the absolute path to an isolated Release publish directory.");
var publish = Path.GetFullPath(args[0]);
Check(File.Exists(Path.Combine(publish, "Aquarella.dll")), "published application exists");
Check(!Directory.Exists(Path.Combine(publish, "App_Data")), "publish does not contain a local database");
var storage = Path.Combine(Path.GetTempPath(), "aquarella-hosting-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(storage);
var database = Path.Combine(storage, "production-test.db");
var keys = Path.Combine(storage, "keys");
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
var options = new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite($"Data Source={database}").Options;
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) {
    BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10)
};
var cookies = new Dictionary<string, string>();
Process? server = null;
var logs = new System.Collections.Concurrent.ConcurrentQueue<string>();
try {
    await Start();
    Check((await Send("GET", "/health", forwarded: false)).StatusCode == HttpStatusCode.OK, "HTTP health probe");
    Check((await Send("GET", "/health", forwarded: false, host: "healthcheck.railway.app")).StatusCode == HttpStatusCode.OK, "Railway healthcheck host allowed");
    Check((await Send("GET", "/health", forwarded: false, host: "untrusted.example")).StatusCode == HttpStatusCode.BadRequest, "unexpected host rejected");
    Check((await Send("GET", "/login", forwarded: false)).StatusCode == HttpStatusCode.BadRequest, "plain HTTP application access rejected");
    var login = await Send("GET", "/login");
    var html = await login.Content.ReadAsStringAsync();
    Check(login.IsSuccessStatusCode && !html.Contains("Entrar sin cuenta"), "Production Login, development button absent");
    foreach (var route in new[] { "/", "/stock", "/precios", "/perfil", "/agenda", "/mi-cuenta" })
        Check((await Send("GET", route)).StatusCode == HttpStatusCode.Redirect, "protected " + route);
    var token = Token(html);
    Check((await Send("POST", "/login?handler=Development", new() { ["__RequestVerificationToken"] = token })).StatusCode == HttpStatusCode.NotFound,
        "valid-CSRF Development endpoint rejected");
    Check(!cookies.ContainsKey("__Host-Aquarella.Auth"), "bypass issues no auth cookie");
    Check((await Send("POST", "/login", new())).StatusCode == HttpStatusCode.BadRequest, "CSRF enforced");
    foreach (var route in new[] { "/crear-cuenta", "/recuperar-contrasena", "/verificar-email" }) {
        var form = await Send("GET", route);
        var body = WebUtility.HtmlDecode(await form.Content.ReadAsStringAsync());
        Check(body.Contains("servicio de correo todavía no está disponible") && !body.Contains("Te enviamos un enlace"), "email unavailable " + route);
        var result = await Send("POST", route, new() { ["__RequestVerificationToken"] = Token(body) });
        Check(WebUtility.HtmlDecode(await result.Content.ReadAsStringAsync()).Contains("No se realizó ningún cambio"), "unavailable email does not pretend success");
    }
    await using (var db = new AquarellaDbContext(options)) {
        Check((await db.Database.GetAppliedMigrationsAsync()).Count() == db.Database.GetMigrations().Count(), "existing migrations applied");
        Check(!await db.Users.AnyAsync() && !await db.AccountTokens.AnyAsync(), "clean Production; no local user/tokens copied");
        // Fixture belongs ONLY to this new temporary database, never to real Production/Development.
        var user = new User { Username = "hosting-verification", Email = "hosting@example.test", NormalizedEmail = "HOSTING@EXAMPLE.TEST", EmailVerified = true };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Isolated fixture password 123");
        var business = new Business { User = user, LegacyImported = true };
        db.Users.Add(user); db.Businesses.Add(business);
        db.Products.Add(new Product { Business = business, Name = "Persisted fixture", Quantity = 7 });
        await db.SaveChangesAsync();
    }
    html = await (await Send("GET", "/login")).Content.ReadAsStringAsync();
    var signedIn = await Send("POST", "/login", new() {
        ["__RequestVerificationToken"] = Token(html), ["Email"] = "hosting@example.test",
        ["Password"] = "Isolated fixture password 123", ["Remember"] = "true"
    });
    Check(signedIn.StatusCode == HttpStatusCode.Redirect, "real credential login");
    Check(signedIn.Headers.GetValues("Set-Cookie").Any(v => v.StartsWith("__Host-Aquarella.Auth=")
        && v.Contains("secure", StringComparison.OrdinalIgnoreCase) && v.Contains("httponly", StringComparison.OrdinalIgnoreCase)), "secure HttpOnly authentication cookie");
    Check((await Send("GET", "/")).IsSuccessStatusCode, "authenticated dashboard");
    for (var i = 0; i < 13; i++) {
        var attempt = await Send("POST", "/login", new(), proxyClientIp: "198.51.100.11");
        Check(attempt.StatusCode == (i < 12 ? HttpStatusCode.BadRequest : HttpStatusCode.TooManyRequests), "IP rate limit through trusted proxy");
    }
    Check((await Send("POST", "/login", new(), proxyClientIp: "198.51.100.12")).StatusCode == HttpStatusCode.BadRequest,
        "different client IP is not throttled by previous client");
    var keyFiles = Directory.GetFiles(keys, "*.xml");
    Check(keyFiles.Length > 0, "Data Protection key generated on persistent path");
    await Stop(); await Start();
    Check((await Send("GET", "/")).IsSuccessStatusCode, "authentication cookie survives process restart");
    Check(keyFiles.All(File.Exists), "Data Protection keys preserved");
    await using (var db = new AquarellaDbContext(options)) {
        Check(await db.Products.AnyAsync(p => p.Name == "Persisted fixture" && p.Quantity == 7), "SQLite data survives process restart");
        Check((await db.Database.GetAppliedMigrationsAsync()).Count() == db.Database.GetMigrations().Count(), "restart does not re-create schema");
    }
    html = await (await Send("GET", "/mi-cuenta")).Content.ReadAsStringAsync();
    Check((await Send("POST", "/mi-cuenta?handler=Logout", new() { ["__RequestVerificationToken"] = Token(html) })).StatusCode == HttpStatusCode.Redirect, "logout");
    Check((await Send("GET", "/stock")).StatusCode == HttpStatusCode.Redirect, "routes protected after logout");
    await Stop(); await Start(trusted: false);
    Check((await Send("GET", "/login")).StatusCode == HttpStatusCode.BadRequest, "untrusted proxy headers ignored");
    Console.WriteLine("PASS: PORT, health, trusted/untrusted proxy, IP rate limiting, Login, protected routes, CSRF, Production bypass rejection, unavailable email, migrations, real cookies, restart persistence and logout.");
    Console.WriteLine("Isolated artifacts retained at: " + storage);
    Console.WriteLine("Native .NET process test only; Docker/Linux and public HTTPS remain separate checks.");
} catch (Exception error) {
    Console.Error.WriteLine("FAIL: " + error.Message);
    var logPath = Path.Combine(storage, "verification-private.log");
    await File.WriteAllLinesAsync(logPath, logs);
    Console.Error.WriteLine("Private isolated logs: " + logPath);
    Environment.ExitCode = 1;
} finally { await Stop(); }

async Task<HttpResponseMessage> Send(string method, string path, Dictionary<string, string>? fields = null, bool forwarded = true, string proxyClientIp = "198.51.100.10", string host = "localhost") {
    using var request = new HttpRequestMessage(new HttpMethod(method), path);
    request.Headers.Host = host;
    if (forwarded) { request.Headers.Add("X-Forwarded-Proto", "https"); request.Headers.Add("X-Real-IP", proxyClientIp); }
    if (cookies.Count > 0) request.Headers.Add("Cookie", string.Join("; ", cookies.Select(p => p.Key + "=" + p.Value)));
    if (fields is not null) request.Content = new FormUrlEncodedContent(fields);
    var response = await http.SendAsync(request);
    if (response.Headers.TryGetValues("Set-Cookie", out var values)) foreach (var value in values) {
        var pair = value.Split(';')[0].Split('=', 2);
        if (pair[1].Length == 0) cookies.Remove(pair[0]); else cookies[pair[0]] = pair[1];
    }
    return response;
}
async Task Start(bool trusted = true) {
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = publish, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(Path.Combine(publish, "Aquarella.dll"));
    foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("ReverseProxy__", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production"; start.Environment["DOTNET_ENVIRONMENT"] = "Production";
    start.Environment["PORT"] = port.ToString(); start.Environment["ProductionStorage__Root"] = storage;
    start.Environment["ConnectionStrings__Aquarella"] = $"Data Source={database}";
    start.Environment["DataProtection__KeysPath"] = keys;
    start.Environment["Accounts__PublicOrigin"] = "https://localhost"; start.Environment["AllowedHosts"] = "localhost;healthcheck.railway.app";
    start.Environment["ReverseProxy__KnownProxies__0"] = trusted ? "127.0.0.1" : "192.0.2.254";
    start.Environment["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "false";
    // The sandbox cannot write Windows Event Log. Linux containers do not have this provider.
    start.Environment["Logging__EventLog__LogLevel__Default"] = "None";
    start.Environment.Remove("RAILWAY_VOLUME_MOUNT_PATH");
    start.Environment.Remove("RAILWAY_SERVICE_ID");
    server = Process.Start(start)!;
    server.OutputDataReceived += (_, e) => { if (e.Data is not null) logs.Enqueue(e.Data); };
    server.ErrorDataReceived += (_, e) => { if (e.Data is not null) logs.Enqueue(e.Data); };
    // Drain private logs without printing token URLs, connection strings or internal exceptions.
    server.BeginOutputReadLine(); server.BeginErrorReadLine();
    for (var i = 0; i < 100; i++) {
        if (server.HasExited) throw new Exception("Isolated server failed to start; exit code " + server.ExitCode);
        try { if ((await Send("GET", "/health", forwarded: false)).IsSuccessStatusCode) return; } catch (HttpRequestException) { }
        await Task.Delay(100);
    }
    throw new Exception("Isolated server did not become healthy.");
}
async Task Stop() {
    if (server is null) return;
    if (!server.HasExited) { server.Kill(entireProcessTree: true); await server.WaitForExitAsync(); }
    server.Dispose(); server = null;
}
static string Token(string body) {
    var token = Regex.Match(body, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    Check(token.Length > 0, "antiforgery form token exists"); return WebUtility.HtmlDecode(token);
}
static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); }
