using Aquarella.Models;
using Microsoft.JSInterop;

namespace Aquarella.Services;

public sealed class BrowserBusinessProfilePersistence(IJSRuntime js) : IBusinessProfilePersistence
{
    public async Task<BusinessProfile?> LoadAsync() =>
        await js.InvokeAsync<BusinessProfile?>("aquarellaIdentity.load");

    public async Task SaveAsync(BusinessProfile profile) =>
        await js.InvokeVoidAsync("aquarellaIdentity.save", profile);
}
