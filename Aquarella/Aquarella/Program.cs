using Aquarella.Data;
using Microsoft.EntityFrameworkCore;
using Aquarella.Components;
using Aquarella.Services;
using Aquarella.Services.Accounts;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
ProductionHosting.Configure(builder);

builder.Services.Configure<OpenAIOptions>(builder.Configuration.GetSection("OpenAI"));
builder.Services.PostConfigure<OpenAIOptions>(options =>
{
    if (string.IsNullOrWhiteSpace(options.ApiKey))
    {
        options.ApiKey = builder.Configuration["OPENAI_API_KEY"];
    }
});
builder.Services.AddHttpClient<AquarellaChatService>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<BusinessProfileStore>();
builder.Services.AddScoped<AgendaStore>();
builder.Services.AddScoped<NoticesStore>();
builder.Services.AddScoped<AiSettingsStore>();
builder.Services.AddScoped<MetricsService>();
builder.Services.AddScoped<AccountSession>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<DevelopmentAccess>();
if (builder.Environment.IsDevelopment()) builder.Services.AddScoped<IAccountEmail, DevelopmentAccountEmail>();
else builder.Services.AddScoped<IAccountEmail, UnavailableAccountEmail>();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, AccountAuthenticationStateProvider>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options => {
    options.Cookie.Name = "__Host-Aquarella.Auth";
    options.Cookie.HttpOnly = true; options.Cookie.SecurePolicy = CookieSecurePolicy.Always; options.Cookie.SameSite = SameSiteMode.Lax;
    options.LoginPath = "/login"; options.AccessDeniedPath = "/login"; options.SlidingExpiration = false;
    options.Events.OnValidatePrincipal = async context => { if (!await context.HttpContext.RequestServices.GetRequiredService<AccountService>().IsSessionValidAsync(context.Principal!)) context.RejectPrincipal(); };
});
builder.Services.AddAuthorization();
builder.Services.AddRazorPages(options => {
    foreach (var route in new[] { "/login", "/crear-cuenta", "/verificar-correo", "/verificar-email", "/recuperar-contrasena", "/nueva-contrasena" }) options.Conventions.AddPageRoute("/Access", route);
}).AddCookieTempDataProvider(options => { options.Cookie.SecurePolicy = CookieSecurePolicy.Always; options.Cookie.HttpOnly = true; });
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.AddPolicy("accounts", context => HttpMethods.IsPost(context.Request.Method)
        ? RateLimitPartition.GetFixedWindowLimiter($"{context.Connection.RemoteIpAddress}:{context.Request.Path}", _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })
        : RateLimitPartition.GetNoLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local"));
    options.OnRejected = async (context, token) => { context.HttpContext.Response.Headers.RetryAfter = "60"; await context.HttpContext.Response.WriteAsync("Demasiados intentos. Esperá un minuto y volvé a intentar.", token); };
});
builder.Services.AddScoped<IBusinessProfilePersistence, DatabaseBusinessProfilePersistence>();
builder.Services.AddScoped<BusinessData>();
builder.Services.AddScoped<IInvoiceExtractor, UnavailableInvoiceExtractor>();
builder.Services.AddSingleton<DatabaseChanges>();
var connection = builder.Configuration.GetConnectionString("Aquarella") ?? "Data Source=App_Data/aquarella.db";
var sqlite = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connection);
if (!Path.IsPathRooted(sqlite.DataSource) && sqlite.DataSource != ":memory:") sqlite.DataSource = Path.Combine(builder.Environment.ContentRootPath, sqlite.DataSource);
Directory.CreateDirectory(Path.GetDirectoryName(sqlite.DataSource)!);
builder.Services.AddDbContextFactory<AquarellaDbContext>(options => options.UseSqlite(sqlite.ToString()));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseForwardedHeaders();
using (var scope = app.Services.CreateScope()) {
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AquarellaDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
    var legacy = await db.Users.SingleOrDefaultAsync(u => u.Username == "pablo" && u.Email == null);
    if (app.Environment.IsDevelopment() && legacy is not null) {
        var link = await scope.ServiceProvider.GetRequiredService<AccountService>().IssueTokenAsync(legacy.Id, "legacy", TimeSpan.FromHours(24));
        await File.WriteAllTextAsync(Path.Combine(app.Environment.ContentRootPath, "App_Data", "development-account-link.txt"), "/crear-cuenta?legacy=" + Uri.EscapeDataString(link));
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(error => error.Run(async context => {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("No se pudo completar la solicitud. Volvé a intentar más tarde.");
    }));
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
// Keep failed form submissions (including antiforgery 400) from being re-executed as protected pages.
app.UseWhen(context => HttpMethods.IsGet(context.Request.Method), branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
// Railway probes the container over HTTP. This response contains no application data.
app.Use(async (context, next) => {
    if (context.Request.Path == "/health" && HttpMethods.IsGet(context.Request.Method)) {
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync("OK");
        return;
    }
    if (!app.Environment.IsDevelopment() && !context.Request.IsHttps) {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Se requiere HTTPS mediante el proxy configurado.");
        return;
    }
    await next();
});
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) => {
    if (app.Environment.IsDevelopment() && Path.GetFileName(sqlite.DataSource) == "accounts-verification.db") context.Response.Headers["X-Aquarella-Isolated-Tests"] = "true";
    context.Response.OnStarting(() => { context.Response.Headers.CacheControl = "no-cache, no-store"; return Task.CompletedTask; });
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorPages().RequireRateLimiting("accounts");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode().RequireAuthorization();

app.Run();




