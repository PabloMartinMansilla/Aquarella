using Aquarella.Data;
using Aquarella.Models;
using Aquarella.Services;
using Aquarella.Services.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.JSInterop;
using System.Security.Claims;
using System.Reflection;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Components;

var path = Path.Combine(Path.GetTempPath(), $"aquarella-reliability-{Guid.NewGuid():N}.db");
var delay = new DelayedWrite();
var factory = new Factory(new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite($"Data Source={path}").AddInterceptors(delay).Options);
try
{
    Guid userId, sessionId, productId;
    await using (var db = factory.CreateDbContext())
    {
        await db.Database.MigrateAsync();
        var user = new User { Username = "reliability-fixture" };
        var business = new Business { User = user, LegacyImported = true };
        var login = new AccountLoginSession { UserId = user.Id, ExpiresAt = DateTime.UtcNow.AddHours(1) };
        var product = new Product { Business = business, Name = "Fixture", Cost = 100, DesiredProfitPercent = 10, Quantity = 2 };
        db.Businesses.Add(business); db.AccountLoginSessions.Add(login); db.Products.Add(product);
        await db.SaveChangesAsync(); userId = user.Id; sessionId = login.Id; productId = product.Id;
    }
    var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim("sid", sessionId.ToString())], "test"));
    var session = new AccountSession(new Auth(principal), new AccountService(factory, new EphemeralDataProtectionProvider(), new Email()), factory);
    var changes = new DatabaseChanges();
    using var data = new BusinessData(factory, session, new Js(), changes);
    using var second = new BusinessData(factory, session, new Js(), changes);
    if (args.Contains("--baseline"))
    {
        var firstView = (await data.LoadPricesAsync()).Single();
        var secondView = (await second.LoadPricesAsync()).Single();
        firstView.Cost = 200; secondView.DesiredProfitPercent = 50;
        await data.SavePriceAsync(firstView); await second.SavePriceAsync(secondView);
        var actual = (await data.LoadPricesAsync()).Single();
        Check(actual.Cost == 200 && actual.DesiredProfitPercent == 50, "Two stale views must preserve both edits");
    }
    else
    {
        await Task.WhenAll(data.ApplyPriceEditAsync(new(productId, PriceField.Cost, 200)), second.ApplyPriceEditAsync(new(productId, PriceField.DesiredProfitPercent, 50)));
        var actual = (await data.LoadPricesAsync()).Single();
        Check(actual.Cost == 200 && actual.DesiredProfitPercent == 50, "Concurrent views preserve unrelated fields");
        await data.ApplyPriceEditAsync(new(productId, PriceField.SalePrice, 450));
        var notifications = 0; changes.Changed += (_, area) => { if (area == "prices") notifications++; };
        await Task.WhenAll(data.ApplyPriceEditAsync(new(productId, PriceField.SalePrice, 450)), second.ApplyPriceEditAsync(new(productId, PriceField.SalePrice, 450)));
        Check(notifications == 0, "Duplicate unchanged edits perform no effective write/notification");
        await data.ApplyPriceEditAsync(new(productId, PriceField.Cost, 220));
        await data.ApplyPriceEditAsync(new(productId, PriceField.Cost, 230));
        Check((await data.LoadPricesAsync()).Single().Cost == 230, "Close ordered edits retain newest value");
        try { await data.ApplyPriceEditAsync(new(productId, PriceField.Cost, -1)); throw new Exception("Invalid edit accepted"); } catch (ArgumentException) { }
        Check((await data.LoadPricesAsync()).Single().Cost == 230, "Invalid edit preserves persisted value");
        await data.ApplyPriceEditAsync(new(productId, PriceField.Suggested));
        actual = (await data.LoadPricesAsync()).Single();
        Check(!actual.ManualSalePrice && actual.EffectiveSalePrice == 345, "Suggested uses latest persisted cost/percent");
        try { await data.ApplyPriceEditAsync(new(Guid.NewGuid(), PriceField.Cost, 10)); throw new Exception("Foreign/missing edit accepted"); } catch (InvalidOperationException) { }
        await data.ApplyPriceEditAsync(new(productId, PriceField.Cost, 240));
        Check((await data.LoadPricesAsync()).Single().Cost == 240, "A failed operation does not block later writes");
        var view = new Aquarella.Components.Pages.Precios();
        Set(view, "Data", data);
        var visible = (await data.LoadPricesAsync()).Single();
        Set(view, "prices", new Dictionary<Guid, ProductPrice> { [productId] = visible });
        delay.Arm();
        var firstEdit = Call(view, "Change", visible, 0, new ChangeEventArgs { Value = "250" });
        await delay.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var newestEdit = Call(view, "Change", visible, 0, new ChangeEventArgs { Value = "260" });
        delay.Release.TrySetResult();
        await Task.WhenAll(firstEdit, newestEdit);
        Check(Get<Dictionary<Guid, ProductPrice>>(view, "prices")[productId].Cost == 260 && (await data.LoadPricesAsync()).Single().Cost == 260, "Actual view handlers: delayed older response cannot overwrite latest edit");
        delay.FailNext = true;
        visible = Get<Dictionary<Guid, ProductPrice>>(view, "prices")[productId];
        await Call(view, "Change", visible, 0, new ChangeEventArgs { Value = "270" });
        Check(Get<Dictionary<Guid, ProductPrice>>(view, "prices")[productId].Cost == 260, "SQL failure restores persisted value in actual view");
        visible = Get<Dictionary<Guid, ProductPrice>>(view, "prices")[productId];
        await Call(view, "Change", visible, 0, new ChangeEventArgs { Value = "275" });
        Check((await data.LoadPricesAsync()).Single().Cost == 275, "View can save again after database failure");
        view.Dispose();
        Console.WriteLine("PASS Precios: concurrent fields, duplicate events, newest value, validation, manual/suggested price and failure recovery.");
        await using (var db = factory.CreateDbContext())
        {
            var business = await db.Businesses.SingleAsync(b => b.UserId == userId);
            business.Profile.Name = "Datos conservados"; business.Profile.PrimaryColor = "invalid"; business.Profile.Phone = "123";
            await db.SaveChangesAsync();
        }
        var profileStore = new BusinessProfileStore(new DatabaseBusinessProfilePersistence(data, new PresentationJs()));
        await profileStore.InitializeAsync();
        var recovered = profileStore.Load();
        Check(profileStore.LoadWarning is not null && recovered.Name == "Datos conservados" && recovered.Phone == "123" && recovered.PrimaryColor == "invalid", "Partial profile retains both valid and invalid original fields for explicit correction");
        try { await profileStore.SaveAsync(recovered); throw new Exception("Invalid profile saved"); } catch (System.ComponentModel.DataAnnotations.ValidationException) { }
        recovered.PrimaryColor = "#F7F9F6"; await profileStore.SaveAsync(recovered);
        Check(profileStore.LoadWarning is null && (await data.LoadProfileAsync()).Phone == "123", "Explicit correction saves without losing valid details");
        await using (var db = factory.CreateDbContext()) { var b = await db.Businesses.SingleAsync(b => b.UserId == userId); b.Profile.Name = ""; b.Profile.SecondaryColor = "invalid"; b.LegacyImported = false; await db.SaveChangesAsync(); }
        var invalidLegacy = new LegacyJs();
        using var importer = new BusinessData(factory, session, invalidLegacy);
        foreach (var malformed in new BusinessData.LegacyData[] { new() { Products = null! }, new() { Products = [null!] }, new() { Notes = [new(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), null!, "", null)] } })
        {
            invalidLegacy.Value = malformed;
            try { await importer.EnsureImportedAsync(); throw new Exception("Malformed legacy accepted"); } catch (Exception e) when (e is ArgumentException or System.ComponentModel.DataAnnotations.ValidationException) { }
            await using var db = factory.CreateDbContext();
            Check(!await db.Businesses.Where(b => b.UserId == userId).Select(b => b.LegacyImported).SingleAsync() && await db.Products.CountAsync() == 1, "Malformed legacy neither marks import complete nor writes partial data");
        }
        invalidLegacy.Value = new(); await importer.EnsureImportedAsync();
        profileStore = new(new DatabaseBusinessProfilePersistence(importer, new PresentationJs()));
        await profileStore.InitializeAsync();
        Check(profileStore.LoadWarning is not null && profileStore.Load().Name == "", "Fully invalid profile loads for correction without inventing identity or silently saving defaults");
        Console.WriteLine("PASS datos malformados: partial/invalid profile, explicit recovery, null legacy, atomic rejection and retry.");
        var disconnectingStore = new BusinessProfileStore(new DatabaseBusinessProfilePersistence(data, new DisconnectedJs()));
        await disconnectingStore.SaveAsync(new() { Name = "Escritura completada" });
        Check(disconnectingStore.Load().Name == "Escritura completada" && (await data.LoadProfileAsync()).Name == "Escritura completada", "JS disconnection after profile commit is not reported as database failure");
        view = new(); Set(view, "Data", data);
        visible = (await data.LoadPricesAsync()).Single(); Set(view, "prices", new Dictionary<Guid, ProductPrice> { [productId] = visible });
        delay.Arm(); firstEdit = Call(view, "Change", visible, 0, new ChangeEventArgs { Value = "300" });
        await delay.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        newestEdit = Call(view, "Change", visible, 0, new ChangeEventArgs { Value = "310" });
        view.Dispose(); delay.Release.TrySetResult(); await Task.WhenAll(firstEdit, newestEdit);
        await Call(view, "Change", visible, 0, new ChangeEventArgs { Value = "999" });
        Check((await data.LoadPricesAsync()).Single().Cost == 310, "Navigation completes accepted writes in order and rejects events from disposed view");
        using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); try { await data.LoadProductsAsync(cancel.Token); throw new Exception("Canceled read continued"); } catch (OperationCanceledException) { } }
        var module = new DisconnectedModule(); await BrowserModuleLifetime.ReleaseAsync(module, "stop");
        Check(module.Disposals == 1, "Module reference is released even when browser stop fails");
        var delayedJs = new DelayedJs();
        var agenda = new Aquarella.Components.Pages.Agenda();
        using var agendaStore = new AgendaStore(data, Microsoft.Extensions.Logging.Abstractions.NullLogger<AgendaStore>.Instance);
        Set(agenda, "Store", agendaStore); Set(agenda, "JS", delayedJs); Set(agenda, "ready", true);
        var loadingModule = Call(agenda, "OnAfterRenderAsync", false);
        await delayedJs.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Call(agenda, "OnAfterRenderAsync", false);
        Check(delayedJs.Calls == 1, "Overlapping renders share one Agenda module import");
        await agenda.DisposeAsync();
        module = new(); delayedJs.Result.TrySetResult(module); await loadingModule;
        Check(module.Disposals == 1 && Get<IJSObjectReference?>(agenda, "popover") is null, "Late module import is disposed without updating abandoned Agenda");
        delayedJs = new(); var metricsView = new Aquarella.Components.Pages.Metricas(); Set(metricsView, "Data", data); Set(metricsView, "JS", delayedJs); Set(metricsView, "motionPending", true);
        loadingModule = Call(metricsView, "OnAfterRenderAsync", false);
        await delayedJs.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); Set(metricsView, "motionPending", true); await Call(metricsView, "OnAfterRenderAsync", false);
        Check(delayedJs.Calls == 1, "Overlapping renders share one metrics module import");
        await metricsView.DisposeAsync(); module = new(); delayedJs.Result.TrySetResult(module); await loadingModule;
        Check(module.Disposals == 1, "Late metrics module is released after navigation");
        delayedJs = new(); var tour = new Aquarella.Components.OnboardingTour(); Set(tour, "Session", session); Set(tour, "JS", delayedJs);
        loadingModule = Call(tour, "OnAfterRenderAsync", true);
        await delayedJs.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); await tour.DisposeAsync(); module = new(); delayedJs.Result.TrySetResult(module); await loadingModule;
        Check(module.Disposals == 1 && Get<IJSObjectReference?>(tour, "module") is null, "Late tutorial import cannot retain an abandoned callback/module");
        Console.WriteLine("PASS navegación: accepted writes finish, canceled reads, profile commit on disconnect, module cleanup and late import after dispose. SignalR reconnect requires manual test.");
    }
}
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static Task Call(object target, string name, params object[] args) => (Task)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args)!;
static void Set(object target, string name, object value) { var type = target.GetType(); var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic); if (property is not null) property.SetValue(target, value); else type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value); }
static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
sealed class DelayedWrite : DbCommandInterceptor
{
    public bool PauseNext;
    public bool FailNext;
    public TaskCompletionSource Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Arm() { PauseNext = true; Started = new(TaskCreationOptions.RunContinuationsAsynchronously); Release = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (FailNext && command.CommandText.StartsWith("UPDATE \"Products\"")) { FailNext = false; throw new Microsoft.Data.Sqlite.SqliteException("Isolated database failure", 5); }
        if (PauseNext && command.CommandText.StartsWith("UPDATE \"Products\"")) { PauseNext = false; Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
        return result;
    }
}
sealed class Factory(DbContextOptions<AquarellaDbContext> options) : IDbContextFactory<AquarellaDbContext> { public AquarellaDbContext CreateDbContext() => new(options); }
sealed class Auth(ClaimsPrincipal principal) : AuthenticationStateProvider { public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal)); }
sealed class Email : IAccountEmail { public bool Available => true; public Task SendAsync(string recipient, string purpose, string link) => Task.CompletedTask; }
sealed class Js : IJSRuntime { public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => throw new Exception("No legacy import expected"); public ValueTask<T> InvokeAsync<T>(string id, CancellationToken token, object?[]? args) => InvokeAsync<T>(id, args); }
sealed class PresentationJs : IJSRuntime { public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => ValueTask.FromResult(default(T)!); public ValueTask<T> InvokeAsync<T>(string id, CancellationToken token, object?[]? args) => InvokeAsync<T>(id, args); }
sealed class DisconnectedJs : IJSRuntime { public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => throw new JSDisconnectedException("Isolated disconnection fixture"); public ValueTask<T> InvokeAsync<T>(string id, CancellationToken token, object?[]? args) => InvokeAsync<T>(id, args); }
sealed class DisconnectedModule : IJSObjectReference { public int Disposals; public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => throw new JSDisconnectedException("Isolated disconnection fixture"); public ValueTask<T> InvokeAsync<T>(string id, CancellationToken token, object?[]? args) => InvokeAsync<T>(id, args); public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; } }
sealed class DelayedJs : IJSRuntime
{
    public int Calls;
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<IJSObjectReference> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async ValueTask<T> InvokeAsync<T>(string id, object?[]? args) { Calls++; Started.TrySetResult(); return (T)(object)await Result.Task; }
    public ValueTask<T> InvokeAsync<T>(string id, CancellationToken token, object?[]? args) => InvokeAsync<T>(id, args);
}
sealed class LegacyJs : IJSRuntime, IJSObjectReference
{
    public BusinessData.LegacyData Value { get; set; } = new();
    public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => ValueTask.FromResult((T)(id == "import" ? (object)this : Value));
    public ValueTask<T> InvokeAsync<T>(string id, CancellationToken token, object?[]? args) => InvokeAsync<T>(id, args);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
