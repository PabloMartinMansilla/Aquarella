using Aquarella.Models;

namespace Aquarella.Services;

public interface IBusinessProfilePersistence
{
    Task<BusinessProfile?> LoadAsync();
    Task SaveAsync(BusinessProfile profile);
}
