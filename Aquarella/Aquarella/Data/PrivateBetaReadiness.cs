using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aquarella.Data;

public sealed record PrivateBetaAssessment(int VerifiedUsers, int PendingUsers, int IncompleteAccounts,
    int ActiveSessions, int UsableTokens, int PendingEmails)
{
    public bool Ready => VerifiedUsers > 0 && PendingUsers == 0 && IncompleteAccounts == 0
        && ActiveSessions == 0 && UsableTokens == 0 && PendingEmails == 0;
}

// Offline preflight only: no account changes, verification bypass, hashing changes or personal-data output.
public static class PrivateBetaReadiness
{
    public static async Task<PrivateBetaAssessment> InspectAsync(string database, CancellationToken cancellationToken = default)
    {
        database = DatabaseMigrationSafety.AbsolutePath(database);
        await DatabaseMigrationSafety.RequireCurrentAsync(database, cancellationToken);
        await using var connection = DatabaseMigrationSafety.Openable(database, SqliteOpenMode.ReadOnly);
        await connection.OpenAsync(cancellationToken);
        await using var db = new AquarellaDbContext(new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite(connection).Options);
        using var snapshot = connection.BeginTransaction(deferred: true);
        await db.Database.UseTransactionAsync(snapshot, cancellationToken);
        var now = DateTime.UtcNow;
        var verified = await db.Users.CountAsync(u => u.EmailVerified, cancellationToken);
        var pending = await db.Users.CountAsync(u => !u.EmailVerified, cancellationToken);
        var incomplete = await db.Users.CountAsync(u => u.Email == null || u.Email == "" || u.NormalizedEmail == null
            || u.NormalizedEmail == "" || u.PasswordHash == null || u.PasswordHash == "" || u.Business == null, cancellationToken);
        var sessions = await db.AccountLoginSessions.CountAsync(s => s.ExpiresAt > now, cancellationToken);
        var tokens = await db.AccountTokens.CountAsync(t => t.UsedAt == null && t.ExpiresAt > now, cancellationToken);
        var emails = await db.AccountEmailNotifications.CountAsync(n => n.SentAt == null && n.CancelledAt == null, cancellationToken);
        return new(verified, pending, incomplete, sessions, tokens, emails);
    }
}
