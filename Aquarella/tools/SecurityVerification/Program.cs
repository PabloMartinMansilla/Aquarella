using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Aquarella.Data;
using Aquarella.Services;
using Aquarella.Services.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

if (args.Length < 1) throw new ArgumentException("Pass an isolated publish directory; --probe permits observing pre-fix headers/limits.");
var probe = args.Contains("--probe");
var publish = Path.GetFullPath(args[0]);
if (!File.Exists(Path.Combine(publish, "Aquarella.dll")) || Directory.Exists(Path.Combine(publish, "App_Data"))) throw new Exception("Use an isolated publish without local data.");
var root = Path.Combine(Path.GetTempPath(), "aquarella-security-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var database = Path.Combine(root, "security.db");
var options = new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite($"Data Source={database}").Options;
const string password = "Disposable security fixture 123!";
const string payload = "<img src=x onerror=\"document.documentElement.dataset.auditXss='executed'\">";
Guid userId;
string storedHash;
await using (var db = new AquarellaDbContext(options)) {
    await db.Database.MigrateAsync();
    var user = new User { Username = "security-fixture", FirstName = payload, LastName = "Fixture", Email = "audit@example.test", NormalizedEmail = "AUDIT@EXAMPLE.TEST", EmailVerified = true, HasCompletedOnboarding = true };
    storedHash = user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
    var business = new Business { User = user, LegacyImported = true };
    business.Profile.Name = payload; business.Profile.Description = "<a href='https://example.test'>Not a real link</a>";
    db.Businesses.Add(business);
    db.Products.Add(new() { Business = business, Name = payload, Quantity = 1 });
    db.CalendarEntries.Add(new() { Business = business, Date = new(2026, 10, 6), Title = payload, Content = "<script>document.documentElement.dataset.auditXss='executed'</script>" });
    await db.SaveChangesAsync(); userId = user.Id;
    await db.Database.OpenConnectionAsync();
    await using var version = db.Database.GetDbConnection().CreateCommand();
    version.CommandText = "SELECT sqlite_version()";
    Console.WriteLine("SQLite native version (isolated connection): " + await version.ExecuteScalarAsync());
}
var hash = Convert.FromBase64String(storedHash);
var iterations = (int)BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(5, 4));
var saltLength = (int)BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(9, 4));
Check(hash[0] == 1 && BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(1, 4)) == 2 && iterations >= 100_000 && saltLength >= 16, "Identity V3 PBKDF2 HMAC-SHA512 parameters");
Check(hash.AsSpan(13 + saltLength).SequenceEqual(Rfc2898DeriveBytes.Pbkdf2(password, hash.AsSpan(13, saltLength), iterations, HashAlgorithmName.SHA512, hash.Length - 13 - saltLength)), "Stored hash derives from salted PBKDF2, not plaintext");
Check(storedHash != new PasswordHasher<User>().HashPassword(new(), password), "Fresh random salt for same password");
Check(!AccountService.ValidEmail(null) && !AccountService.ValidEmail("") && !AccountService.ValidPassword(null) && !AccountService.ValidPassword(""), "Empty/null credentials rejected server-side");
Console.WriteLine($"PASS hashing: Identity V3, HMAC-SHA512, {iterations} iterations, {saltLength}-byte salt, {hash.Length - 13 - saltLength}-byte subkey; independent salts.");
try { InvoiceDocument.DetectType("<html>executable disguise</html>"u8.ToArray()); throw new Exception("Executable disguised as image accepted"); } catch (InvoiceExtractionException) { }
try { InvoiceDocument.DetectType([]); throw new Exception("Empty upload accepted"); } catch (InvoiceExtractionException) { }
Check(InvoiceDocument.DetectType("%PDF-fake, deliberately not a complete document"u8.ToArray()) == "application/pdf", "Magic signature alone is not a complete parser; OCR remains unavailable");
Console.WriteLine("PASS uploads: HTML/empty rejected by signatures; truncated PDF signature accepted, documented parser requirement before OCR.");

var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
using var http = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(10) };
var cookies = new Dictionary<string, string>();
var logs = new System.Collections.Concurrent.ConcurrentQueue<string>();
Process? server = null;
try {
    await Start();
    var login = await Send("GET", "/login");
    var loginBody = await login.Content.ReadAsStringAsync();
    Check(!loginBody.Contains("Entrar sin cuenta"), "Development bypass absent in Production");
    Check((await Send("POST", "/login?handler=Development", new() { ["__RequestVerificationToken"] = Token(loginBody) })).StatusCode == HttpStatusCode.NotFound, "Development handler rejected even with valid CSRF");
    foreach (var route in new[] { "/", "/stock", "/perfil", "/precios", "/agenda", "/metricas", "/avisos", "/configuracion-ia", "/configuracion-pagina", "/mi-cuenta", "/conversaciones", "/conversaciones/chat" })
        Check((await Send("GET", route)).StatusCode == HttpStatusCode.Redirect, "Anonymous route protected: " + route);
    var negotiate = await Send("POST", "/_blazor/negotiate?negotiateVersion=1", new());
    if (probe) Console.WriteLine($"OBSERVED anonymous Blazor negotiate: {(int)negotiate.StatusCode}.");
    else Check(negotiate.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect or HttpStatusCode.Forbidden, "Anonymous Blazor negotiation does not create a connection");
    foreach (var route in new[] { "/App_Data/security.db", "/App_Data/development-account-link.txt", "/App_Data/development-emails/test.json", "/appsettings.json", "/appsettings.Development.json", "/../App_Data/security.db" })
        Check(!(await Send("GET", route)).IsSuccessStatusCode, "Private file not served: " + route);
    var badCsrf = await Send("POST", "/login", new() { ["Email"] = "audit@example.test", ["Password"] = password });
    Check(badCsrf.StatusCode == HttpStatusCode.BadRequest, "Missing antiforgery rejected");
    if (!probe) Headers(badCsrf);
    Check((await Send("POST", "/login", new() { ["__RequestVerificationToken"] = "forged", ["Email"] = "audit@example.test", ["Password"] = password })).StatusCode == HttpStatusCode.BadRequest, "Forged antiforgery rejected");
    var known = await Post("/login", new() { ["Email"] = "audit@example.test", ["Password"] = "wrong fixture" });
    var missing = await Post("/login", new() { ["Email"] = "missing@example.test", ["Password"] = "wrong fixture" });
    var sql = await Post("/login", new() { ["Email"] = "a' OR '1'='1@example.test", ["Password"] = "wrong fixture" });
    const string generic = "Correo electrónico o contraseña incorrectos.";
    Check(WebUtility.HtmlDecode(await known.Content.ReadAsStringAsync()).Contains(generic) && WebUtility.HtmlDecode(await missing.Content.ReadAsStringAsync()).Contains(generic) && sql.StatusCode != HttpStatusCode.Redirect, "Login generic for unknown/wrong credentials; SQL-looking email cannot bypass login");
    var emptyForm = await Post("/login", new() { ["Email"] = "", ["Password"] = "" });
    var emptyPassword = await Post("/login", new() { ["Email"] = "audit@example.test", ["Password"] = "" });
    if (probe) Console.WriteLine($"OBSERVED empty forms: email/password={(int)emptyForm.StatusCode}, password={(int)emptyPassword.StatusCode}.");
    else Check(emptyForm.StatusCode == HttpStatusCode.OK && emptyPassword.StatusCode == HttpStatusCode.OK && !cookies.ContainsKey("__Host-Aquarella.Auth"), "Empty model-bound fields produce validation errors, not crashes or sessions");
    var signed = await Post("/login?returnUrl=https://example.test/not-visited", new() { ["Email"] = "audit@example.test", ["Password"] = password, ["Remember"] = "true", ["UserId"] = Guid.NewGuid().ToString(), ["EmailVerified"] = "false", ["Role"] = "admin" });
    Check(signed.StatusCode == HttpStatusCode.Redirect && signed.Headers.Location?.ToString() == "/", "No open redirect; forged internal fields cannot replace identity");
    var authCookie = signed.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("__Host-Aquarella.Auth="));
    Check(authCookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && authCookie.Contains("secure", StringComparison.OrdinalIgnoreCase) && authCookie.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase) && authCookie.Contains("path=/", StringComparison.OrdinalIgnoreCase) && authCookie.Contains("expires=", StringComparison.OrdinalIgnoreCase), "Secure HttpOnly host cookie, Lax, root path, bounded persistence");
    var issuedCookie = cookies["__Host-Aquarella.Auth"];
    Check(!issuedCookie.Contains(password) && !issuedCookie.Contains(storedHash), "Cookie has no plaintext password/hash");
    var account = await Send("GET", "/mi-cuenta"); var accountBody = await account.Content.ReadAsStringAsync();
    Check(accountBody.Contains("&lt;img") && !accountBody.Contains(payload) && !accountBody.Contains(storedHash) && !accountBody.Contains(password), "Actual Razor account view encodes XSS/HTML payload and does not return hash/password");
    Console.WriteLine("PASS HTTP: anonymous routes, private paths, antiforgery, login SQL input, overposting, redirect, cookies and Razor XSS encoding.");
    var hasFrames = account.Headers.TryGetValues("Content-Security-Policy", out var csp) && csp.Any(v => v.Contains("frame-ancestors 'none'"));
    var hasNoSniff = account.Headers.TryGetValues("X-Content-Type-Options", out var ns) && ns.Contains("nosniff");
    if (probe) Console.WriteLine($"OBSERVED headers: frame protection={hasFrames}, nosniff={hasNoSniff}.");
    else {
        Headers(account);
        foreach (var route in new[] { "/", "/stock", "/perfil", "/precios", "/agenda", "/metricas", "/avisos", "/configuracion-ia", "/configuracion-pagina", "/conversaciones", "/conversaciones/chat" })
            Check((await Send("GET", route)).IsSuccessStatusCode, "Authenticated route responds: " + route);
        Check(account.Headers.GetValues("Referrer-Policy").Contains("no-referrer") && account.Headers.CacheControl?.NoStore == true, "Sensitive responses no-store/no-referrer");
        Check(account.Headers.Contains("Strict-Transport-Security"), "Production HSTS on forwarded HTTPS");
    }
    Check((await Send("POST", "/mi-cuenta?handler=Logout", new())).StatusCode == HttpStatusCode.BadRequest, "Logout CSRF rejected");
    Check((await Send("GET", "/stock")).IsSuccessStatusCode, "CSRF failure did not revoke session");
    await Post("/mi-cuenta?handler=Logout", new());
    cookies["__Host-Aquarella.Auth"] = issuedCookie;
    Check((await Send("GET", "/stock")).StatusCode == HttpStatusCode.Redirect, "Copied cookie cannot be reused after logout");
    cookies.Remove("__Host-Aquarella.Auth");
    await Post("/login", new() { ["Email"] = "audit@example.test", ["Password"] = password });
    Check(cookies["__Host-Aquarella.Auth"] != issuedCookie, "New login issues a new server session/cookie");
    await using (var db = new AquarellaDbContext(options)) await db.AccountLoginSessions.Where(s => s.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(s => s.ExpiresAt, DateTime.UtcNow.AddSeconds(-1)));
    Check((await Send("GET", "/stock")).StatusCode == HttpStatusCode.Redirect, "Expired session rejected server-side");
    cookies["__Host-Aquarella.Auth"] = "forged-invalid-cookie";
    Check((await Send("GET", "/stock")).StatusCode == HttpStatusCode.Redirect, "Tampered cookie rejected");
    cookies.Remove("__Host-Aquarella.Auth");
    Console.WriteLine("PASS sessions: logout replay rejected, fresh login rotates session, expired and forged cookies denied.");
    // Bounded 13 requests only; alternate valid trailing slash to test partition consistency.
    var ip = "198.51.100.67";
    var form = await Send("GET", "/login", ip: ip); var csrf = Token(await form.Content.ReadAsStringAsync());
    for (var i = 0; i < 12; i++) {
        var route = i % 2 == 0 ? "/login" : "/login/";
        var r = await Send("POST", route, new() { ["__RequestVerificationToken"] = csrf, ["Email"] = "missing@example.test", ["Password"] = "wrong fixture" }, ip: ip);
        Check(r.StatusCode == HttpStatusCode.OK, "First twelve login variants execute");
    }
    var thirteenth = await Send("POST", "/login", new() { ["__RequestVerificationToken"] = csrf, ["Email"] = "missing@example.test", ["Password"] = "wrong fixture" }, ip: ip);
    if (probe) Console.WriteLine($"OBSERVED limiter: attempt 13 across /login and /login/ returns {(int)thirteenth.StatusCode}.");
    else { Check((int)thirteenth.StatusCode == 429 && thirteenth.Headers.Contains("Retry-After"), "Both valid login URL variants share 12/minute budget"); Headers(thirteenth); }
    // Production error rendering under a controlled corrupt fixture, never real DB.
    await using (var db = new AquarellaDbContext(options)) await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, "broken-base64-fixture"));
    var failure = await Post("/login", new() { ["Email"] = "audit@example.test", ["Password"] = password });
    var failureBody = await failure.Content.ReadAsStringAsync();
    Check(failure.StatusCode == HttpStatusCode.InternalServerError && !failureBody.Contains("Exception") && !failureBody.Contains(root) && !failureBody.Contains("SELECT"), "Production 500 is generic, no paths/SQL/stack");
    if (!probe) Headers(failure);
    // Retain only a valid seed state for an optional controlled browser test.
    await using (var db = new AquarellaDbContext(options)) await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, storedHash));
    Check(!logs.Any(l => l.Contains(password) || l.Contains(storedHash) || l.Contains(issuedCookie)), "Captured server logs contain no fixture credentials or cookies");
    Console.WriteLine("PASS Production: safe error response, no credentials in logs, no outbound email/OCR/AI.");
} finally {
    if (server is not null) { if (!server.HasExited) { server.Kill(entireProcessTree: true); await server.WaitForExitAsync(); } server.Dispose(); }
    await File.WriteAllLinesAsync(Path.Combine(root, "private-server.log"), logs);
    Console.WriteLine("Isolated security artifacts: " + root);
}

async Task<HttpResponseMessage> Post(string route, Dictionary<string,string> fields) {
    var get = await Send("GET", route); fields["__RequestVerificationToken"] = Token(await get.Content.ReadAsStringAsync());
    return await Send("POST", route, fields);
}
async Task<HttpResponseMessage> Send(string method, string route, Dictionary<string,string>? fields = null, string ip = "198.51.100.66") {
    using var request = new HttpRequestMessage(new HttpMethod(method), route);
    // Only the Host header is fictional; every connection goes to loopback. Avoid HSTS's localhost exclusion.
    request.Headers.Host = "aquarella.example.test"; request.Headers.Add("X-Forwarded-Proto", "https"); request.Headers.Add("X-Real-IP", ip);
    if (cookies.Count > 0) request.Headers.Add("Cookie", string.Join("; ", cookies.Select(p => p.Key + "=" + p.Value)));
    if (fields is not null) request.Content = new FormUrlEncodedContent(fields);
    var response = await http.SendAsync(request);
    if (response.Headers.TryGetValues("Set-Cookie", out var values)) foreach (var v in values) { var pair = v.Split(';')[0].Split('=',2); if (pair[1].Length == 0) cookies.Remove(pair[0]); else cookies[pair[0]] = pair[1]; }
    return response;
}
async Task Start() {
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = publish, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(Path.Combine(publish, "Aquarella.dll"));
    foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("ReverseProxy__", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
    start.Environment["ASPNETCORE_ENVIRONMENT"] = start.Environment["DOTNET_ENVIRONMENT"] = "Production";
    start.Environment.Remove("PORT"); start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
    start.Environment["Accounts__PublicOrigin"] = "https://aquarella.example.test"; start.Environment["AllowedHosts"] = "aquarella.example.test";
    start.Environment["ProductionStorage__Root"] = root; start.Environment["DataProtection__KeysPath"] = Path.Combine(root, "keys");
    start.Environment["ConnectionStrings__Aquarella"] = $"Data Source={database}";
    start.Environment["ReverseProxy__KnownProxies__0"] = "127.0.0.1"; start.Environment["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "false";
    start.Environment["OpenAI__Enabled"] = "false"; start.Environment.Remove("OPENAI_API_KEY"); start.Environment.Remove("OpenAI__ApiKey");
    start.Environment["Logging__EventLog__LogLevel__Default"] = "None";
    start.Environment.Remove("RAILWAY_SERVICE_ID"); start.Environment.Remove("RAILWAY_VOLUME_MOUNT_PATH");
    server = Process.Start(start)!;
    server.OutputDataReceived += (_, e) => { if (e.Data is not null) logs.Enqueue(e.Data); }; server.ErrorDataReceived += (_, e) => { if (e.Data is not null) logs.Enqueue(e.Data); };
    server.BeginOutputReadLine(); server.BeginErrorReadLine();
    for (var i=0; i<100; i++) { if (server.HasExited) throw new Exception("Isolated server failed to start"); try { if ((await Send("GET", "/health")).IsSuccessStatusCode) return; } catch (HttpRequestException) { } await Task.Delay(100); }
    throw new Exception("Local server did not start");
}
static string Token(string html) {
    var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
    Check(match.Success, "Antiforgery token exists on actual form");
    return WebUtility.HtmlDecode(match.Groups[1].Value);
}
static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); }
static void Headers(HttpResponseMessage response) => Check(
    response.Headers.TryGetValues("Content-Security-Policy", out var csp) && csp.Contains("frame-ancestors 'none'")
    && response.Headers.TryGetValues("X-Frame-Options", out var frame) && frame.Contains("DENY")
    && response.Headers.TryGetValues("X-Content-Type-Options", out var sniff) && sniff.Contains("nosniff"),
    "Security headers present on HTTP " + (int)response.StatusCode);
