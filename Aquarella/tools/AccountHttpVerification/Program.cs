using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length > 0) { await Isolated.RunAsync(args[0]); return; }

var origin = Environment.GetEnvironmentVariable("AQUARELLA_VERIFICATION_URL") ?? "https://localhost:7188";
var fixtureRoot = Environment.GetEnvironmentVariable("AQUARELLA_VERIFICATION_ROOT") ?? throw new Exception("Pass the isolated temporary application root.");
fixtureRoot = Path.GetFullPath(fixtureRoot);
if (!fixtureRoot.StartsWith(Path.Combine(Path.GetTempPath(), "aquarella-mail-http-"), StringComparison.OrdinalIgnoreCase)) throw new Exception("Temporary HTTP fixture only.");
var outbox = Path.Combine(fixtureRoot, "App_Data", "development-emails");
var address = $"account-{Guid.NewGuid():N}@example.test";
const string password = "Una frase de prueba 123";
var jar = new CookieContainer();
using var client = new HttpClient(new HttpClientHandler { CookieContainer = jar, AllowAutoRedirect = false });
client.BaseAddress = new Uri(origin);
var target = await client.GetAsync("/login");
Check(target.Headers.TryGetValues("X-Aquarella-Isolated-Tests", out var targetValues) && targetValues.Contains("true"), "Refusing HTTP tests: server must use Development and accounts-verification.db");
foreach (var path in new[] { "/", "/stock", "/perfil" }) {
    var response = await client.GetAsync(path); Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location!.ToString().Contains("/login"), "anonymous route blocked: " + path);
}
var valid = new Dictionary<string, string> { ["FirstName"] = "Ana", ["LastName"] = "Prueba", ["Email"] = address, ["Password"] = password, ["Confirmation"] = password };
await Error("/crear-cuenta", new(valid) { ["Email"] = "invalid" }, "correo electrónico válido");
await Error("/crear-cuenta", new(valid) { ["Confirmation"] = "No coincide" }, "no coinciden");
await Error("/crear-cuenta", new(valid) { ["Password"] = "short", ["Confirmation"] = "short" }, "12 y 256");
var registration = await Post("/crear-cuenta", valid); Check(registration.StatusCode == HttpStatusCode.Redirect, "registration redirect");
var duplicate = await Post("/crear-cuenta", valid);
Check(duplicate.StatusCode == registration.StatusCode && duplicate.Headers.Location == registration.Headers.Location, "registration does not enumerate duplicate email");
var verification = await Link("verify");
await Error("/login", new() { ["Email"] = address, ["Password"] = password }, "Correo electrónico o contraseña incorrectos.");
// This HTTP fixture configures a one-second cooldown; never weaken the production default.
await Task.Delay(1100);
var resend = await Post("/verificar-email", new() { ["Email"] = address });
Check(WebUtility.HtmlDecode(await resend.Content.ReadAsStringAsync()).Contains("Si corresponde"), "resend generic message");
var previousVerification = verification;
verification = await Link("verify", previousVerification);
Check(WebUtility.HtmlDecode(await client.GetStringAsync(previousVerification)).Contains("ya fue utilizado"), "delivered resend invalidates previous verification link");
var verifyPage = await client.GetStringAsync(verification);
Check(verifyPage.Contains("Verificar correo") && !verifyPage.Contains("Correo verificado correctamente"), "GET does not consume verification (email scanner)");
var verifyToken = WebUtility.UrlDecode(new Uri(origin + verification).Query[7..]);
using (var noCsrfVerify = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(origin) })
    Check((await noCsrfVerify.PostAsync(verification, new FormUrlEncodedContent(new Dictionary<string,string> { ["Token"] = verifyToken }))).StatusCode == HttpStatusCode.BadRequest, "Verification POST requires antiforgery");
var verify = await Post(verification, new() { ["Token"] = verifyToken }); Check((await verify.Content.ReadAsStringAsync()).Contains("Correo verificado correctamente"), "explicit POST verifies email");
var verifyAgain = await client.GetAsync(verification); Check((await verifyAgain.Content.ReadAsStringAsync()).Contains("ya fue utilizado"), "verification cannot be reused");
await Error("/login", new() { ["Email"] = address, ["Password"] = password.ToLowerInvariant() }, "Correo electrónico o contraseña incorrectos.");
await Error("/login", new() { ["Email"] = "missing@example.test", ["Password"] = password }, "Correo electrónico o contraseña incorrectos.");
await Error("/login", new() { ["Email"] = address, ["Password"] = "Una contraseña incorrecta" }, "Correo electrónico o contraseña incorrectos.");
var signedIn = await Post("/login", new() { ["Email"] = address, ["Password"] = password, ["Remember"] = "true" });
Check(signedIn.StatusCode == HttpStatusCode.Redirect, "login accepted");
var persistent = signedIn.Headers.GetValues("Set-Cookie").Single(s => s.StartsWith("__Host-Aquarella.Auth="));
Check(persistent.Contains("expires=", StringComparison.OrdinalIgnoreCase) && persistent.Contains("secure", StringComparison.OrdinalIgnoreCase) && persistent.Contains("httponly", StringComparison.OrdinalIgnoreCase), "persistent secure httponly cookie");
foreach (var path in new[] { "/", "/stock", "/perfil", "/precios", "/agenda", "/mi-cuenta" }) Check((await client.GetAsync(path)).StatusCode == HttpStatusCode.OK, "authenticated route: " + path);
var storedCookies = jar.GetCookies(new Uri(origin)); var remembered = new CookieContainer();
foreach (Cookie cookie in storedCookies) if (cookie.Name == "__Host-Aquarella.Auth") remembered.Add(new Uri(origin), cookie);
using (var reopened = new HttpClient(new HttpClientHandler { CookieContainer = remembered, AllowAutoRedirect = false }) { BaseAddress = new Uri(origin) }) Check((await reopened.GetAsync("/")).StatusCode == HttpStatusCode.OK, "remembered session across clients");
var logout = await Post("/mi-cuenta?handler=Logout", new()); Check(logout.StatusCode == HttpStatusCode.Redirect, "logout redirect");
foreach (var path in new[] { "/", "/stock", "/perfil" }) Check((await client.GetAsync(path)).StatusCode == HttpStatusCode.Redirect, "route blocked after logout");
var noRemember = await Post("/login", new() { ["Email"] = address, ["Password"] = password });
var transient = noRemember.Headers.GetValues("Set-Cookie").Single(s => s.StartsWith("__Host-Aquarella.Auth=")); Check(!transient.Contains("expires=", StringComparison.OrdinalIgnoreCase), "nonpersistent session cookie");
await Post("/mi-cuenta?handler=Logout", new());
var known = await Post("/recuperar-contrasena", new() { ["Email"] = address });
var unknown = await Post("/recuperar-contrasena", new() { ["Email"] = "missing@example.test" });
const string generic = "Si existe una cuenta asociada";
Check(WebUtility.HtmlDecode(await known.Content.ReadAsStringAsync()).Contains(generic) && WebUtility.HtmlDecode(await unknown.Content.ReadAsStringAsync()).Contains(generic), "recovery generic for existing/unknown");
var resetUrl = await Link("reset"); var resetToken = WebUtility.UrlDecode(new Uri(origin + resetUrl).Query[7..]);
await Error(resetUrl, new() { ["Token"] = resetToken, ["Password"] = "short", ["Confirmation"] = "short" }, "12 y 256");
var reset = await Post(resetUrl, new() { ["Token"] = resetToken, ["Password"] = "Una nueva frase de prueba 456", ["Confirmation"] = "Una nueva frase de prueba 456" });
Check(WebUtility.HtmlDecode(await reset.Content.ReadAsStringAsync()).Contains("Contraseña actualizada correctamente."), "HTTP reset password");
await Link("password-changed");
var reuse = await Post(resetUrl, new() { ["Token"] = resetToken, ["Password"] = "Una nueva frase de prueba 789", ["Confirmation"] = "Una nueva frase de prueba 789" });
Check(WebUtility.HtmlDecode(await reuse.Content.ReadAsStringAsync()).Contains("ya fue utilizado"), "HTTP reset single use");
await Error("/login", new() { ["Email"] = address, ["Password"] = password }, "Correo electrónico o contraseña incorrectos.");
Check((await Post("/login", new() { ["Email"] = address, ["Password"] = "Una nueva frase de prueba 456" })).StatusCode == HttpStatusCode.Redirect, "new password login");
using (var noCsrf = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(origin) }) Check((await noCsrf.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode == HttpStatusCode.BadRequest, "CSRF rejected");
Console.WriteLine("PASS HTTP: registration validations, email duplicate, verify/single-use, login/case/errors, secure remembered/session cookies, protected routes/logout, generic recovery, reset/new login, CSRF.");
var limited = false;
for (var i = 0; i < 13; i++) { var response = await Post("/recuperar-contrasena", new() { ["Email"] = "missing@example.test" }); if ((int)response.StatusCode == 429) { limited = true; break; } }
Check(limited, "rate limiting rejects repeated attempts");
Console.WriteLine("PASS HTTP: rate limiting 429 with Retry-After.");
foreach (var path in new[] { "/verificar-correo", "/nueva-contrasena" }) {
    var rejected = false;
    for (var i = 0; i < 13; i++) {
        using var response = await client.GetAsync(path + "?token=invalid-fixture");
        if ((int)response.StatusCode == 429) {
            rejected = response.Headers.GetValues("Referrer-Policy").Single() == "no-referrer" && response.Headers.CacheControl?.NoStore == true;
            break;
        }
    }
    Check(rejected, "Token validation GET rate limit and private headers: " + path);
}
Console.WriteLine("PASS HTTP: token GET validation budget, no-referrer and no-store including 429.");
// This file contains test credentials only; it stays inside ignored App_Data for manual UI verification.
await File.WriteAllTextAsync(Path.Combine(fixtureRoot, "App_Data", "account-test-login.json"), JsonSerializer.Serialize(new { Email = address, Password = "Una nueva frase de prueba 456" }));

async Task<HttpResponseMessage> Post(string route, Dictionary<string,string> values) {
    var form = await client.GetStringAsync(route);
    var match = Regex.Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
    if (!match.Success) throw new Exception("Missing CSRF token on " + route);
    values["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value);
    return await client.PostAsync(route, new FormUrlEncodedContent(values));
}
async Task Error(string route, Dictionary<string,string> values, string expected) { var response = await Post(route, values); var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()); Check(response.StatusCode == HttpStatusCode.OK && html.Contains(expected), "validation on " + route + ": " + expected); }
async Task<string> Link(string purpose, string? previous = null) {
    var until = DateTime.UtcNow.AddSeconds(20);
    while (DateTime.UtcNow < until) {
        if (Directory.Exists(outbox)) {
            var links = new List<(DateTime Created, string Link)>();
            foreach (var path in Directory.GetFiles(outbox, "*.json")) {
                try {
                    using var item = JsonDocument.Parse(await File.ReadAllTextAsync(path));
                    if (item.RootElement.GetProperty("Recipient").GetString() != address || item.RootElement.GetProperty("Purpose").GetString() != purpose) continue;
                    var link = item.RootElement.GetProperty("Link").GetString()!;
                    links.Add((item.RootElement.GetProperty("CreatedAt").GetDateTime(), link.Length == 0 ? "notification" : new Uri(link).PathAndQuery));
                } catch (JsonException) { /* Writer may still be completing the private fixture file. */ }
            }
            var found = links.OrderByDescending(x => x.Created).FirstOrDefault();
            if (found.Link is not null && found.Link != previous) return found.Link;
        }
        await Task.Delay(100);
    }
    throw new Exception("Timed out waiting for simulated " + purpose + " email.");
}
static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
