using Aquarella.Models;
using Microsoft.JSInterop;
namespace Aquarella.Services;
public sealed class DatabaseBusinessProfilePersistence(BusinessData data, IJSRuntime js, ILogger<DatabaseBusinessProfilePersistence>? logger = null) : IBusinessProfilePersistence
{
    public async Task<BusinessProfile?> LoadAsync() {
        var profile = await data.LoadProfileAsync();
        await DisplayAsync(profile);
        return profile;
    }
    public async Task<BusinessProfile> SaveAsync(BusinessProfile profile, BusinessProfile original) {
        var actual = await data.SaveProfileAsync(profile, original);
        await DisplayAsync(actual);
        return actual;
    }
    private async Task DisplayAsync(BusinessProfile profile)
    {
        try { await js.InvokeVoidAsync("aquarellaIdentity.display", profile); }
        catch (JSDisconnectedException) { /* Database values remain authoritative; the next view loads them. */ }
        catch (JSException e) { logger?.LogWarning("Business identity presentation failed ({ErrorType}); database values were retained.", e.GetType().Name); }
    }
}
