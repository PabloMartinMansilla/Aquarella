using Aquarella.Models;

namespace Aquarella.Services;

public interface IBusinessProfilePersistence
{
    Task<BusinessProfile?> LoadAsync();
    Task<BusinessProfile> SaveAsync(BusinessProfile profile, BusinessProfile original);
}
