using Aquarella.Data;
using Aquarella.Models;
using Aquarella.Services.Accounts;
using Microsoft.EntityFrameworkCore;

namespace Aquarella.Services;

// Uses the existing authenticated session and SQLite context; no provider or data-access capability.
public sealed class AiSettingsStore(IDbContextFactory<AquarellaDbContext> factory, AccountSession session)
{
    public async Task<AiSettings> LoadAsync()
    {
        var id = await session.RequireUserAsync();
        await using var db = await factory.CreateDbContextAsync();
        var json = await db.Users.AsNoTracking().Where(user => user.Id == id)
            .Select(user => user.AiSettingsJson).SingleAsync();
        return AiSettingsCodec.Read(json);
    }
    public async Task SaveAsync(AiSettings settings)
    {
        var id = await session.RequireUserAsync();
        var json = AiSettingsCodec.Write(settings);
        await using var db = await factory.CreateDbContextAsync();
        var updated = await db.Users.Where(user => user.Id == id).ExecuteUpdateAsync(update => update
            .SetProperty(user => user.AiSettingsJson, json).SetProperty(user => user.UpdatedAt, DateTime.UtcNow));
        if (updated != 1) throw new InvalidOperationException("No se pudo guardar la configuración de tu cuenta.");
    }
}
