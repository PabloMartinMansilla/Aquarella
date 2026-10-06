using Aquarella.Models;
using System.ComponentModel.DataAnnotations;

namespace Aquarella.Services;

// One saved identity per circuit, backed by a replaceable persistence adapter.
public sealed class BusinessProfileStore(IBusinessProfilePersistence persistence, ILogger<BusinessProfileStore>? logger = null)
{
    private BusinessProfile saved = new();
    private Task? initialization;
    public event Action? Changed;
    public string? LoadWarning { get; private set; }
    public BusinessProfile Load() => saved.Copy();
    public Task InitializeAsync()
    {
        if (initialization is null || initialization.IsFaulted || initialization.IsCanceled)
            initialization = InitializeCoreAsync();
        return initialization;
    }

    private async Task InitializeCoreAsync()
    {
        var loaded = await persistence.LoadAsync();
        if (loaded is not null)
        {
            saved = loaded.Copy();
            LoadWarning = IsValid(loaded) ? null : "El perfil guardado contiene campos inválidos. Conservamos los datos; corregilos en Perfil antes de guardar.";
            if (LoadWarning is not null) logger?.LogWarning("Stored business profile requires correction; original values were retained.");
        }
        Changed?.Invoke();
    }

    public async Task SaveAsync(BusinessProfile profile)
    {
        if (!IsValid(profile)) throw new ValidationException("El perfil contiene datos inválidos.");
        var snapshot = profile.Copy();
        await persistence.SaveAsync(snapshot);
        saved = snapshot;
        LoadWarning = null;
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

