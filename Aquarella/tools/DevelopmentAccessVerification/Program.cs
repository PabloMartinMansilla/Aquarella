using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
var production = args.Contains("--production");
var origin = Environment.GetEnvironmentVariable("AQUARELLA_VERIFICATION_URL") ?? (production ? "https://localhost:7190" : "https://localhost:7188");
var dbPath = Path.GetFullPath("Aquarella/Aquarella/App_Data/development-access-verification.db");
var before = production ? "" : await Snapshot();
using var client = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = new Uri(origin) };
var html = await client.GetStringAsync("/login");
Check(html.Contains("Entrar sin cuenta") != production, "button visibility by environment");
var token = Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
var result = await client.PostAsync("/login?handler=Development", new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token) }));
if (production) {
    Check(result.StatusCode == HttpStatusCode.NotFound, "Production handler rejected");
    Check(!result.Headers.TryGetValues("Set-Cookie", out var values) || !values.Any(s=>s.StartsWith("__Host-Aquarella.Auth=")), "Production issues no authentication cookie");
    Check((await client.GetAsync("/stock")).StatusCode == HttpStatusCode.Redirect, "Production routes still protected");
    Console.WriteLine("PASS Production: button absent, valid-CSRF bypass rejected 404, no auth cookie, Stock protected."); return;
}
Check(result.StatusCode == HttpStatusCode.Redirect && result.Headers.Location!.ToString() == "/", "development login redirects dashboard");
Check(result.Headers.GetValues("Set-Cookie").Any(s=>s.StartsWith("__Host-Aquarella.Auth=") && s.Contains("httponly",StringComparison.OrdinalIgnoreCase)), "real authentication cookie");
foreach(var path in new[]{"/","/perfil","/stock","/precios","/agenda","/mi-cuenta"}) Check((await client.GetAsync(path)).StatusCode == HttpStatusCode.OK, "authenticated route " + path);
await Logout(); Check((await client.GetAsync("/stock")).StatusCode == HttpStatusCode.Redirect,"Stock blocked after logout");
html = await client.GetStringAsync("/login"); token=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
Check((await client.PostAsync("/login?handler=Development",new FormUrlEncodedContent(new Dictionary<string,string>{["__RequestVerificationToken"]=WebUtility.HtmlDecode(token)}))).StatusCode == HttpStatusCode.Redirect,"second development login");
Check(before == await Snapshot(),"users/businesses/profile/stock/prices/agenda/tutorial unchanged");
await Logout(); Console.WriteLine("PASS Development: real cookie, routes, logout/re-entry, no duplicated users/businesses, data and tutorial unchanged.");
async Task Logout(){var form=await client.GetStringAsync("/mi-cuenta");var csrf=Regex.Match(form,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;Check((await client.PostAsync("/mi-cuenta?handler=Logout",new FormUrlEncodedContent(new Dictionary<string,string>{["__RequestVerificationToken"]=WebUtility.HtmlDecode(csrf)}))).StatusCode==HttpStatusCode.Redirect,"logout");}
async Task<string> Snapshot(){ using var connection=new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly"); await connection.OpenAsync(); var rows=new List<object[]>();foreach(var query in new[]{"SELECT * FROM Users ORDER BY Id","SELECT * FROM Businesses ORDER BY Id","SELECT * FROM Products ORDER BY Id","SELECT * FROM CalendarEntries ORDER BY Id"}){using var command=connection.CreateCommand();command.CommandText=query;using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync()){var row=new object[reader.FieldCount];reader.GetValues(row);rows.Add(row);}}return System.Text.Json.JsonSerializer.Serialize(rows);}
static void Check(bool value,string message){if(!value)throw new Exception(message);}
