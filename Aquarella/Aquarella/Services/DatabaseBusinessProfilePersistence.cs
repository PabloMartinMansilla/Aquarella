using Aquarella.Models;
using Microsoft.JSInterop;
namespace Aquarella.Services;
public sealed class DatabaseBusinessProfilePersistence(BusinessData data, IJSRuntime js) : IBusinessProfilePersistence
{
    public async Task<BusinessProfile?> LoadAsync() {
        var profile = await data.LoadProfileAsync();
        await js.InvokeVoidAsync("aquarellaIdentity.display", profile);
        return profile;
    }
    public async Task SaveAsync(BusinessProfile profile) {
        await data.SaveProfileAsync(profile);
        await js.InvokeVoidAsync("aquarellaIdentity.display", profile);
    }
}
