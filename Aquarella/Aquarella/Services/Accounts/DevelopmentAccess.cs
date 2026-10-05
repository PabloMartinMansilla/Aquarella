using Aquarella.Data;
using Microsoft.EntityFrameworkCore;
namespace Aquarella.Services.Accounts;

// No user creation or mutation: this only resolves the configured existing development owner.
public sealed class DevelopmentAccess(IDbContextFactory<AquarellaDbContext> factory, IWebHostEnvironment environment, IConfiguration configuration)
{
    public async Task<User?> FindUserAsync() {
        if (!environment.IsDevelopment()) return null;
        var username = configuration["DevelopmentAccess:Username"];
        if (string.IsNullOrWhiteSpace(username)) return null;
        await using var db = await factory.CreateDbContextAsync();
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == username && u.Business != null);
    }
}
