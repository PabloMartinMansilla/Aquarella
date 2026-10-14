using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Text.Json;
using Aquarella.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Aquarella.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

if (args.Length is < 1 or > 2 || args.Length == 2 && args[1] != "--railway")
    throw new ArgumentException("Pass the absolute path to an isolated Release publish directory.");
var railwayMode = args.Length == 2;
var fixtureHost = railwayMode ? "proxy-fixture.up.railway.app" : "localhost";
var publish = Path.GetFullPath(args[0]);
Check(File.Exists(Path.Combine(publish, "Aquarella.dll")), "published application exists");
Check(!Directory.Exists(Path.Combine(publish, "App_Data")), "publish does not contain a local database");
var storage = Path.Combine(Path.GetTempPath(), "aquarella-hosting-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(storage);
var database = Path.Combine(storage, "production-test.db");
var keys = Path.Combine(storage, "keys");
// Configuration-only checks never load User Secrets or access the application database.
foreach (var (proxy, network) in new (string?, string?)[] { (null, null), ("0.0.0.0", null), ("::", null), ("not-an-ip", null),
    (null, "0.0.0.0/0"), (null, "::/0"), (null, "invalid") }) {
    var invalid = HostingBuilder(proxy, network);
    try { ProductionHosting.Configure(invalid); throw new Exception("Unsafe proxy configuration accepted"); }
    catch (InvalidOperationException) { }
}
foreach (var (proxy, network) in new (string?, string?)[] { ("127.0.0.1", null), (null, "127.0.0.1/32") })
    ProductionHosting.Configure(HostingBuilder(proxy, network));
var implicitTrust = HostingBuilder("127.0.0.1", null);
implicitTrust.Configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true";
try { ProductionHosting.Configure(implicitTrust); throw new Exception("Unrestricted forwarded headers accepted"); }
catch (InvalidOperationException) { }
// Railway metadata is a deployment gate, not proof of peer identity or network topology.
foreach (var missing in new[] { "RAILWAY_PROJECT_ID", "RAILWAY_ENVIRONMENT_ID", "RAILWAY_SERVICE_ID", "RAILWAY_VOLUME_MOUNT_PATH", "PORT", "RAILWAY_PUBLIC_DOMAIN" }) {
    var invalid = RailwayBuilder(); invalid.Configuration[missing] = null;
    Reject(invalid, "missing Railway metadata " + missing);
}
var staging = RailwayBuilder(); staging.Environment.EnvironmentName = "Staging";
Reject(staging, "Railway mode outside Production");
var mixed = RailwayBuilder(); mixed.Configuration["ReverseProxy:KnownProxies:0"] = "127.0.0.1";
Reject(mixed, "mixed trust modes");
var hostMismatch = RailwayBuilder(); hostMismatch.Configuration["RAILWAY_PUBLIC_DOMAIN"] = "other.up.railway.app";
Reject(hostMismatch, "domain/origin mismatch");
var extraHost = RailwayBuilder(); extraHost.Configuration["AllowedHosts"] += ";extra.example.test";
Reject(extraHost, "extra allowed host");
var badFlag = RailwayBuilder(); badFlag.Configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true";
Reject(badFlag, "global forwarded headers in Railway mode");
foreach (var tcpSetting in new[] { "RAILWAY_TCP_PROXY_DOMAIN", "RAILWAY_TCP_PROXY_PORT", "RAILWAY_TCP_APPLICATION_PORT" }) {
    var exposed = RailwayBuilder(); exposed.Configuration[tcpSetting] = "fixture-tcp-value";
    Reject(exposed, "public TCP proxy " + tcpSetting);
}
var disabled = RailwayBuilder(); disabled.Configuration["ReverseProxy:RailwayDedicatedEnvironmentEnabled"] = "false";
Reject(disabled, "disabled mode still requires verified proxies");
var development = RailwayBuilder(); development.Environment.EnvironmentName = "Development";
var serviceCount = development.Services.Count;
ProductionHosting.Configure(development);
Check(development.Services.Count == serviceCount, "Development ignores Railway mode and retains existing hosting");
ProductionHosting.Configure(RailwayBuilder());
WebApplicationBuilder RailwayBuilder() {
    var builder = HostingBuilder(null, null);
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
        ["ReverseProxy:RailwayDedicatedEnvironmentEnabled"] = "true",
        ["RAILWAY_PROJECT_ID"] = "fixture-project", ["RAILWAY_ENVIRONMENT_ID"] = "fixture-environment",
        ["RAILWAY_SERVICE_ID"] = "fixture-service", ["RAILWAY_VOLUME_MOUNT_PATH"] = storage,
        ["RAILWAY_PUBLIC_DOMAIN"] = "proxy-fixture.up.railway.app", ["PORT"] = "18080",
        ["Accounts:PublicOrigin"] = "https://proxy-fixture.up.railway.app",
        ["AllowedHosts"] = "proxy-fixture.up.railway.app;healthcheck.railway.app"
    });
    return builder;
}
static void Reject(WebApplicationBuilder builder, string scenario) {
    try { ProductionHosting.Configure(builder); throw new Exception("Unsafe configuration accepted: " + scenario); }
    catch (InvalidOperationException) { }
}
if (!OperatingSystem.IsWindows()) {
    var privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    Check(File.GetUnixFileMode(storage) == privateMode, "Linux volume directory permissions 0700");
    Check(File.GetUnixFileMode(keys) == privateMode, "Linux Data Protection directory permissions 0700");
}
WebApplicationBuilder HostingBuilder(string? proxy, string? network) {
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production", ContentRootPath = publish });
    builder.Configuration.Sources.Clear();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
        ["Accounts:PublicOrigin"] = "https://localhost", ["AllowedHosts"] = "localhost;healthcheck.railway.app",
        ["ProductionStorage:Root"] = storage, ["DataProtection:KeysPath"] = keys,
        ["ConnectionStrings:Aquarella"] = $"Data Source={database}",
        ["ReverseProxy:KnownProxies:0"] = proxy, ["ReverseProxy:KnownNetworks:0"] = network
    }.Where(pair => pair.Value is not null));
    return builder;
}
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
    // Production startup validates only; this explicit preparation targets this verifier's new TEMP path.
    await DatabaseMaintenance.ApplyAsync(database, Path.Combine(storage, "migration-backups"), confirmStopped: true, allowCreate: true);
    await Start();
    using (var duplicate = Process.Start(server!.StartInfo)!) {
        var duplicateOutput = duplicate.StandardOutput.ReadToEndAsync();
        var duplicateError = duplicate.StandardError.ReadToEndAsync();
        try { await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { if (!duplicate.HasExited) { duplicate.Kill(entireProcessTree: true); await duplicate.WaitForExitAsync(); } }
        Check(duplicate.ExitCode != 0 && (await duplicateOutput + await duplicateError).Contains("un mantenimiento ya utiliza la base"),
            "a second real Production process is rejected by the database lease before binding a port");
    }
    Check((await Send("GET", "/health", forwarded: false)).StatusCode == HttpStatusCode.OK, "HTTP health probe");
    Check((await Send("GET", "/health", forwarded: false, host: "healthcheck.railway.app")).StatusCode == HttpStatusCode.OK, "Railway healthcheck host allowed");
    Check((await Send("GET", "/health", forwarded: false, host: "untrusted.example")).StatusCode == HttpStatusCode.BadRequest, "unexpected host rejected");
    Check((await Send("GET", "/login", forwarded: false)).StatusCode == HttpStatusCode.BadRequest, "plain HTTP application access rejected");
    using (var incomplete = new HttpRequestMessage(HttpMethod.Get, "/login")) {
        incomplete.Headers.Host = fixtureHost;
        incomplete.Headers.Add("X-Forwarded-Proto", "https");
        Check((await http.SendAsync(incomplete)).StatusCode == HttpStatusCode.BadRequest, "incomplete forwarded header pair is rejected");
    }
    var login = await Send("GET", "/login");
    using (var forged = new HttpRequestMessage(HttpMethod.Get, "/stock")) {
        forged.Headers.Host = fixtureHost;
        forged.Headers.Add("X-Forwarded-Proto", "https");
        forged.Headers.Add("X-Real-IP", "198.51.100.10");
        forged.Headers.Add("X-Forwarded-Host", "attacker.example.test");
        forged.Headers.Add("X-Forwarded-For", "203.0.113.99");
        using var response = await http.SendAsync(forged);
        Check(response.StatusCode == HttpStatusCode.Redirect, "trusted HTTPS proxy reaches protected route");
        Check(response.Headers.Location is { } location && !location.ToString().Contains("attacker.example.test")
            && (!location.IsAbsoluteUri || location.Scheme == "https" && location.Host == fixtureHost),
            "forged forwarded host cannot control the HTTPS login redirect");
    }
    using (var insecure = new HttpRequestMessage(HttpMethod.Get, "/login")) {
        insecure.Headers.Host = fixtureHost;
        insecure.Headers.Add("X-Forwarded-Proto", "http");
        insecure.Headers.Add("X-Real-IP", "198.51.100.10");
        using var response = await http.SendAsync(insecure);
        Check(response.StatusCode == HttpStatusCode.BadRequest, "trusted proxy cannot authorize insecure HTTP");
    }
    if (railwayMode) await CheckRailwayHeaders();
    var html = await login.Content.ReadAsStringAsync();
    Check(login.IsSuccessStatusCode && !html.Contains("Entrar sin cuenta") && !html.Contains("href=\"/crear-cuenta\""), "Private Production Login, development/registration links absent");
    foreach (var route in new[] { "/", "/stock", "/precios", "/perfil", "/agenda", "/mi-cuenta" })
        Check((await Send("GET", route)).StatusCode == HttpStatusCode.Redirect, "protected " + route);
    var token = Token(html);
    Check((await Send("POST", "/login?handler=Development", new() { ["__RequestVerificationToken"] = token })).StatusCode == HttpStatusCode.NotFound,
        "valid-CSRF Development endpoint rejected");
    Check(!cookies.ContainsKey("__Host-Aquarella.Auth"), "bypass issues no auth cookie");
    Check((await Send("POST", "/login", new())).StatusCode == HttpStatusCode.BadRequest, "CSRF enforced");
    var registration = WebUtility.HtmlDecode(await (await Send("GET", "/crear-cuenta")).Content.ReadAsStringAsync());
    Check(registration.Contains("La beta es privada") && !registration.Contains("name=\"FirstName\""), "direct registration GET is closed");
    var refused = await Send("POST", "/crear-cuenta", new() {
        ["__RequestVerificationToken"] = Token((await (await Send("GET", "/login")).Content.ReadAsStringAsync())),
        ["FirstName"] = "Private", ["LastName"] = "Fixture", ["Email"] = "private@example.test",
        ["Password"] = "Isolated fixture password 123", ["Confirmation"] = "Isolated fixture password 123"
    });
    Check(WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()).Contains("La beta es privada"), "valid-CSRF direct registration POST is closed");
    Check((await Send("POST", "/crear-cuenta", new())).StatusCode == HttpStatusCode.BadRequest, "closed registration retains CSRF protection");
    foreach (var route in new[] { "/recuperar-contrasena", "/verificar-email" }) {
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
        business.Profile.Name = "Hosting Business A";
        business.Profile.Description = "Restart fixture";
        business.Profile.PrimaryColor = "#F4EFE9";
        business.Profile.LogoDataUrl = "data:image/png;base64,aGVsbG8=";
        user.HasCompletedOnboarding = true;
        user.AiSettingsJson = Aquarella.Services.AiSettingsCodec.Write(new() { AiCustomInstructions = "Restart preference A" });
        db.Users.Add(user); db.Businesses.Add(business);
        db.Products.Add(new Product { Business = business, Name = "Persisted fixture", Quantity = 7, Cost = 2000.50m, DesiredProfitPercent = 35m, SalePrice = 3000.75m, ManualSalePrice = true });
        db.CalendarEntries.Add(new() { Business = business, Date = new(2026, 10, 15), Title = "Restart agenda A", Content = "Restart content", Time = "15:30" });
        var otherUser = new User { Username = "hosting-other", Email = "hosting-other@example.test", NormalizedEmail = "HOSTING-OTHER@EXAMPLE.TEST", EmailVerified = true };
        otherUser.PasswordHash = new PasswordHasher<User>().HashPassword(otherUser, "Isolated fixture password 123");
        var otherBusiness = new Business { User = otherUser, LegacyImported = true };
        otherBusiness.Profile.Name = "Hosting Business B";
        db.Businesses.Add(otherBusiness); db.Products.Add(new() { Business = otherBusiness, Name = "Exclusive B fixture", Quantity = 31, Cost = 20.25m });
        await db.SaveChangesAsync();
    }
    html = await (await Send("GET", "/login")).Content.ReadAsStringAsync();
    var signedIn = await Send("POST", "/login", new() {
        ["__RequestVerificationToken"] = Token(html), ["Email"] = "hosting@example.test",
        ["Password"] = "Isolated fixture password 123", ["Remember"] = "true"
    });
    Check(signedIn.StatusCode == HttpStatusCode.Redirect, "real credential login");
    Check(signedIn.Headers.GetValues("Set-Cookie").Any(v => v.StartsWith("__Host-Aquarella.Auth=")
        && v.Contains("secure", StringComparison.OrdinalIgnoreCase) && v.Contains("httponly", StringComparison.OrdinalIgnoreCase)
        && v.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase) && v.Contains("path=/", StringComparison.OrdinalIgnoreCase)
        && !v.Contains("domain=", StringComparison.OrdinalIgnoreCase)), "secure host-only HttpOnly SameSite authentication cookie");
    Check((await Send("GET", "/")).IsSuccessStatusCode, "authenticated dashboard");
    for (var i = 0; i < 13; i++) {
        var attempt = await Send("POST", "/login", new(), proxyClientIp: "198.51.100.11");
        Check(attempt.StatusCode == (i < 12 ? HttpStatusCode.BadRequest : HttpStatusCode.TooManyRequests), "IP rate limit through trusted proxy");
    }
    Check((await Send("POST", "/login", new(), proxyClientIp: "198.51.100.12")).StatusCode == HttpStatusCode.BadRequest,
        "different client IP is not throttled by previous client");
    var keyFiles = Directory.GetFiles(keys, "*.xml");
    Check(keyFiles.Length > 0, "Data Protection key generated on persistent path");
    string beforeRestart;
    await using (var db = new AquarellaDbContext(options)) beforeRestart = await BusinessSnapshot(db);
    await Stop(); await Start();
    Check((await Send("GET", "/")).IsSuccessStatusCode, "authentication cookie survives process restart");
    Check(keyFiles.All(File.Exists), "Data Protection keys preserved");
    await using (var db = new AquarellaDbContext(options)) {
        Check(await db.Products.AnyAsync(p => p.Name == "Persisted fixture" && p.Quantity == 7), "SQLite data survives process restart");
        Check((await db.Database.GetAppliedMigrationsAsync()).Count() == db.Database.GetMigrations().Count(), "restart does not re-create schema");
        Check(await BusinessSnapshot(db) == beforeRestart, "all business/profile/pricing/agenda/AI/tutorial data survive real server restart exactly");
    }
    html = await (await Send("GET", "/mi-cuenta")).Content.ReadAsStringAsync();
    Check((await Send("POST", "/mi-cuenta?handler=Logout", new() { ["__RequestVerificationToken"] = Token(html) })).StatusCode == HttpStatusCode.Redirect, "logout");
    Check((await Send("GET", "/stock")).StatusCode == HttpStatusCode.Redirect, "routes protected after logout");
    foreach (var address in new[] { "hosting-other@example.test", "hosting@example.test" })
    {
        html = await (await Send("GET", "/login")).Content.ReadAsStringAsync();
        Check((await Send("POST", "/login", new() { ["__RequestVerificationToken"] = Token(html), ["Email"] = address, ["Password"] = "Isolated fixture password 123" })).StatusCode == HttpStatusCode.Redirect, "A/B re-login with real cookies");
        html = await (await Send("GET", "/mi-cuenta")).Content.ReadAsStringAsync();
        var foreignAddress = address == "hosting@example.test" ? "hosting-other@example.test" : "hosting@example.test";
        Check(html.Contains(address) && !html.Contains(foreignAddress), "account page contains only current user after A/B switch");
        Check((await Send("POST", "/mi-cuenta?handler=Logout", new() { ["__RequestVerificationToken"] = Token(html) })).StatusCode == HttpStatusCode.Redirect, "A/B logout");
    }
    await Stop(); await Start(trusted: false);
    var directPeer = await Send("GET", "/login");
    Check(railwayMode ? directPeer.IsSuccessStatusCode : directPeer.StatusCode == HttpStatusCode.BadRequest,
        railwayMode ? "documented residual risk: a reachable peer imitating the edge is indistinguishable" : "untrusted proxy headers ignored");
    await Stop(); await Start();
    // A damaged readiness dependency must produce 503, not a false healthy response or a new DB.
    await using (var db = new AquarellaDbContext(options)) await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"__EFMigrationsHistory\" RENAME TO \"VerifierHistory\"");
    Check((await Send("GET", "/health", forwarded: false)).StatusCode == HttpStatusCode.ServiceUnavailable, "health detects missing schema without repairing it");
    await using (var db = new AquarellaDbContext(options)) await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"VerifierHistory\" RENAME TO \"__EFMigrationsHistory\"");
    Check((await Send("GET", "/health", forwarded: false)).StatusCode == HttpStatusCode.OK, "health recovers after isolated dependency restoration");
    if (!railwayMode) {
        await Stop();
        var child = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = storage };
        child.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
        child.ArgumentList.Add(publish); child.ArgumentList.Add("--railway");
        using var railwayVerifier = Process.Start(child)!;
        var output = railwayVerifier.StandardOutput.ReadToEndAsync(); var errorOutput = railwayVerifier.StandardError.ReadToEndAsync();
        try { await railwayVerifier.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2)); }
        finally { if (!railwayVerifier.HasExited) { railwayVerifier.Kill(entireProcessTree: true); await railwayVerifier.WaitForExitAsync(); } }
        // Only controlled fixture diagnostics; no HTTP bodies/cookies/headers printed.
        Check(railwayVerifier.ExitCode == 0, "Railway isolated HTTP verifier: " + await errorOutput);
        await output;
        Console.WriteLine("PASS: dedicated Railway mode; strict headers/Host, IPv4/IPv6, ignored forwarded Host/For, cookies, CSRF, rate limits, private registration, restart and documented peer risk.");
    }
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

static async Task<string> BusinessSnapshot(AquarellaDbContext db) => JsonSerializer.Serialize(new {
    Users = await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => new { u.Id, u.Username, u.HasCompletedOnboarding, u.AiSettingsJson }).ToListAsync(),
    Businesses = await db.Businesses.AsNoTracking().OrderBy(b => b.Id).Select(b => new { b.Id, b.UserId, b.LegacyImported, b.Profile }).ToListAsync(),
    Products = await db.Products.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.BusinessId, p.Name, p.Quantity, p.Cost, p.DesiredProfitPercent, p.SalePrice, p.ManualSalePrice }).ToListAsync(),
    Notes = await db.CalendarEntries.AsNoTracking().OrderBy(n => n.Id).Select(n => new { n.Id, n.BusinessId, n.Date, n.Title, n.Content, n.Time }).ToListAsync(),
    Receipts = await db.StockIntakeReceipts.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.BusinessId, r.OperationKey, r.ResultsJson }).ToListAsync()
});

async Task<HttpResponseMessage> Send(string method, string path, Dictionary<string, string>? fields = null, bool forwarded = true, string proxyClientIp = "198.51.100.10", string? host = null) {
    using var request = new HttpRequestMessage(new HttpMethod(method), path);
    request.Headers.Host = host ?? fixtureHost;
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
async Task CheckRailwayHeaders() {
    var ignoredClientNumber = 0;
    foreach (var proto in new[] { Array.Empty<string>(), new[] { "http" }, new[] { "HTTPS" }, new[] { "ftp" }, new[] { "https,http" }, new[] { "https", "https" } })
        Check((await Raw(proto, ["198.51.100.70"])).StatusCode == HttpStatusCode.BadRequest, "invalid/missing/duplicate Railway scheme");
    foreach (var ip in new[] { "not-an-ip", "198.51.100.70:80", "198.51.100.70,203.0.113.1", "0.0.0.0", "::", "127.1", "2130706433", "0x7f000001", "fe80::1%1", "[::1]", "224.0.0.1", "ff02::1", "::ffff:0.0.0.0" })
        Check((await Raw(["https"], [ip])).StatusCode == HttpStatusCode.BadRequest, "invalid Railway IP representation");
    Check((await Raw(["https"], [])).StatusCode == HttpStatusCode.BadRequest, "missing Railway IP");
    Check((await Raw(["https"], ["198.51.100.70", "198.51.100.71"])).StatusCode == HttpStatusCode.BadRequest, "duplicate Railway IP");
    foreach (var ip in new[] { "198.51.100.70", "2001:db8::70", "2001:0db8:0000:0000:0000:0000:0000:0070", "::ffff:198.51.100.70" })
        Check((await Raw(["https"], [ip])).IsSuccessStatusCode, "valid standard IPv4/IPv6 Railway client");
    Check((await Send("GET", "/login", host: fixtureHost + ":443")).IsSuccessStatusCode, "explicit HTTPS port accepted");
    Check((await Send("GET", "/login", host: fixtureHost + ":8080")).StatusCode == HttpStatusCode.BadRequest, "unexpected public port rejected");
    Check((await Send("GET", "/login", host: "healthcheck.railway.app")).StatusCode == HttpStatusCode.BadRequest, "health host cannot access application");
    Check((await Send("GET", "/health", forwarded: false, host: "healthcheck.railway.app:" + port)).StatusCode == HttpStatusCode.OK, "health host with actual internal port accepted");
    Check((await Send("GET", "/login", host: "healthcheck.railway.app:" + port)).StatusCode == HttpStatusCode.BadRequest, "internal health authority cannot access application");
    Check((await Send("POST", "/health", forwarded: false, host: "healthcheck.railway.app")).StatusCode == HttpStatusCode.BadRequest, "only GET health bypasses forwarded headers");
    Check((await Raw(["https"], ["198.51.100.70"], host: "attacker.example.test", ignoredHeaders: true)).StatusCode == HttpStatusCode.BadRequest,
        "forwarded Host cannot rescue unexpected original Host");
    using (var forwarded = await Raw(["https"], ["198.51.100.70"], path: "/stock", ignoredHeaders: true))
        Check(forwarded.StatusCode == HttpStatusCode.Redirect && forwarded.Headers.Location is { } location
            && location.Scheme == "https" && location.Host == fixtureHost && location.AbsolutePath == "/login",
            "Host/For/Prefix/Forwarded ignored; challenge uses original canonical Host");
    for (var i = 0; i < 13; i++) {
        using var attempt = await Raw(["https"], [i % 2 == 0 ? "198.51.100.81" : "::ffff:198.51.100.81"], path: "/login", method: "POST", ignoredHeaders: true);
        Check(attempt.StatusCode == (i < 12 ? HttpStatusCode.BadRequest : HttpStatusCode.TooManyRequests),
            "normalized client IP retains one budget despite alternate representations and forged X-Forwarded-For");
    }
    Check((await Raw(["https"], ["198.51.100.82"], method: "POST")).StatusCode == HttpStatusCode.BadRequest, "independent client IP budget");

    // Loopback-only reverse proxy simulates the SUPPLIED edge replacement contract.
    // It does not establish the actual Host/headers behavior of Railway's live edge.
    var proxyBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production", ContentRootPath = storage });
    proxyBuilder.Configuration.Sources.Clear(); proxyBuilder.Configuration.AddInMemoryCollection(); proxyBuilder.Logging.ClearProviders();
    proxyBuilder.WebHost.UseUrls("http://127.0.0.1:0");
    await using var proxy = proxyBuilder.Build();
    proxy.Run(async context => {
        using var upstream = new HttpRequestMessage(HttpMethod.Get, new Uri(http.BaseAddress!, "/stock"));
        upstream.Headers.Host = context.Request.Host.Value;
        foreach (var key in new[] { "X-Forwarded-Proto", "X-Real-IP", "X-Forwarded-Host", "X-Forwarded-For" })
            upstream.Headers.TryAddWithoutValidation(key, context.Request.Headers[key].ToArray());
        upstream.Headers.Remove("X-Forwarded-Proto"); upstream.Headers.Add("X-Forwarded-Proto", "https");
        upstream.Headers.Remove("X-Real-IP"); upstream.Headers.Add("X-Real-IP", "198.51.100.90");
        upstream.Headers.Remove("X-Forwarded-Host"); upstream.Headers.Add("X-Forwarded-Host", context.Request.Host.Value);
        upstream.Headers.Remove("X-Forwarded-For"); upstream.Headers.Add("X-Forwarded-For", "198.51.100.90");
        using var result = await http.SendAsync(upstream);
        context.Response.StatusCode = (int)result.StatusCode;
        if (result.Headers.Location is { } location) context.Response.Headers.Location = location.ToString();
    });
    await proxy.StartAsync();
    try {
        using var edgeClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(10) };
        using var forged = new HttpRequestMessage(HttpMethod.Get, proxy.Urls.Single());
        forged.Headers.Host = fixtureHost;
        forged.Headers.Add("X-Forwarded-Proto", "http"); forged.Headers.Add("X-Real-IP", "bad-ip");
        forged.Headers.Add("X-Forwarded-Host", "attacker.example.test"); forged.Headers.Add("X-Forwarded-For", "203.0.113.9");
        using var result = await edgeClient.SendAsync(forged);
        Check(result.StatusCode == HttpStatusCode.Redirect && result.Headers.Location is { } location
            && location.Scheme == "https" && location.Host == fixtureHost, "simulated edge replaces spoofed public-client headers");
    } finally { await proxy.StopAsync(); }

    async Task<HttpResponseMessage> Raw(string[] proto, string[] ip, string? host = null, string path = "/login", string method = "GET", bool ignoredHeaders = false) {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Host = host ?? fixtureHost;
        if (proto.Length > 0) request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", proto);
        if (ip.Length > 0) request.Headers.TryAddWithoutValidation("X-Real-IP", ip);
        if (ignoredHeaders) {
            request.Headers.Add("X-Forwarded-Host", fixtureHost); request.Headers.Add("X-Forwarded-For", "203.0.113." + ++ignoredClientNumber);
            request.Headers.Add("X-Forwarded-Prefix", "/evil"); request.Headers.Add("Forwarded", "for=203.0.113.99;proto=http;host=attacker.example.test");
        }
        if (method == "POST") request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());
        return await http.SendAsync(request);
    }
}
async Task Start(bool trusted = true) {
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = publish, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(Path.Combine(publish, "Aquarella.dll"));
    foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("ReverseProxy__", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production"; start.Environment["DOTNET_ENVIRONMENT"] = "Production";
    start.Environment["Email__Smtp__Enabled"] = "false";
    start.Environment["Accounts__Registration__Enabled"] = "false";
    start.Environment["PORT"] = port.ToString(); start.Environment["ProductionStorage__Root"] = storage;
    start.Environment["ConnectionStrings__Aquarella"] = $"Data Source={database}";
    start.Environment["DataProtection__KeysPath"] = keys;
    start.Environment["Accounts__PublicOrigin"] = "https://" + fixtureHost; start.Environment["AllowedHosts"] = fixtureHost + ";healthcheck.railway.app";
    if (!railwayMode) start.Environment["ReverseProxy__KnownProxies__0"] = trusted ? "127.0.0.1" : "192.0.2.254";
    start.Environment["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "false";
    // The sandbox cannot write Windows Event Log. Linux containers do not have this provider.
    start.Environment["Logging__EventLog__LogLevel__Default"] = "None";
    foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("RAILWAY_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
    if (railwayMode) {
        start.Environment["ReverseProxy__RailwayDedicatedEnvironmentEnabled"] = "true";
        start.Environment["RAILWAY_PROJECT_ID"] = "fixture-project"; start.Environment["RAILWAY_ENVIRONMENT_ID"] = "fixture-environment";
        start.Environment["RAILWAY_SERVICE_ID"] = "fixture-service"; start.Environment["RAILWAY_VOLUME_MOUNT_PATH"] = storage;
        start.Environment["RAILWAY_PUBLIC_DOMAIN"] = fixtureHost;
    }
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
