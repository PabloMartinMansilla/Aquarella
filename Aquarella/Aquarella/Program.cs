using Aquarella.Components;
using Aquarella.Services;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddScoped<LocalSessionStore>();
builder.Services.AddScoped<IBusinessProfilePersistence, BrowserBusinessProfilePersistence>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();


