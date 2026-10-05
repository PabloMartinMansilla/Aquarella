using Aquarella.Models;
using System.ComponentModel.DataAnnotations;

namespace Aquarella.Services;

// One saved identity per circuit, backed by a replaceable persistence adapter.
public sealed class BusinessProfileStore(IBusinessProfilePersistence persistence)
{
    private BusinessProfile saved = new();
    private Task? initialization;
    public event Action? Changed;
    public BusinessProfile Load() => saved.Copy();
    public Task InitializeAsync() => initialization ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        var loaded = await persistence.LoadAsync();
        if (loaded is not null && IsValid(loaded)) saved = loaded.Copy();
        Changed?.Invoke();
    }

    public async Task SaveAsync(BusinessProfile profile)
    {
        if (!IsValid(profile)) throw new ValidationException("El perfil contiene datos inválidos.");
        var snapshot = profile.Copy();
        await persistence.SaveAsync(snapshot);
        saved = snapshot;
        Changed?.Invoke();
    }

    internal static bool IsValid(BusinessProfile profile) =>
        Validator.TryValidateObject(profile, new ValidationContext(profile), null, true)
        && !string.IsNullOrWhiteSpace(profile.Name)
        && !string.IsNullOrWhiteSpace(profile.PrimaryColor)
        && !string.IsNullOrWhiteSpace(profile.SecondaryColor)
        && !string.IsNullOrWhiteSpace(profile.TertiaryColor)
        && (profile.LogoDataUrl is null || (profile.LogoDataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal)
            && profile.LogoDataUrl.Length <= 3_000_000));
}

