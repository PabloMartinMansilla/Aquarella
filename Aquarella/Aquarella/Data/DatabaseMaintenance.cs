using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aquarella.Data;

public sealed record DatabaseBackup(string Path, string Schema, DatabaseDataStamp Data);
public sealed record MigrationOutcome(string Status, string? BackupPath, string ReceiptPath);

// Operational utility, not an HTTP endpoint. Explicit paths and offline acknowledgement are mandatory.
public static class DatabaseMaintenance
{
    public static async Task<DatabaseBackup> BackupAsync(string source, string destination, CancellationToken cancellationToken = default)
    {
        source = DatabaseMigrationSafety.AbsolutePath(source); destination = DatabaseMigrationSafety.AbsolutePath(destination);
        RequirePrivateBackupPath(destination);
        if (Path.GetExtension(destination).ToLowerInvariant() is not (".db" or ".sqlite" or ".sqlite3"))
            throw new InvalidOperationException("Usá extensión .db, .sqlite o .sqlite3 para que los respaldos permanezcan ignorados por Git.");
        if (!File.Exists(source)) throw new InvalidOperationException("No existe la base a respaldar.");
        // CreateNew rejects existing files, aliases and accidental overwrite of the source.
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using (new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        await using var reader = DatabaseMigrationSafety.Openable(source, SqliteOpenMode.ReadOnly);
        await reader.OpenAsync(cancellationToken);
        using var transaction = reader.BeginTransaction(deferred: true);
        await DatabaseMigrationSafety.IntegrityAsync(reader, transaction, cancellationToken);
        var schema = await DatabaseMigrationSafety.SchemaAsync(reader, transaction, cancellationToken);
        var data = await DatabaseMigrationSafety.DataAsync(reader, transaction, cancellationToken: cancellationToken);
        await using (var writer = DatabaseMigrationSafety.Openable(destination, SqliteOpenMode.ReadWrite)) {
            await writer.OpenAsync(cancellationToken);
            // SQLite's backup API incorporates committed WAL pages from the source read snapshot.
            reader.BackupDatabase(writer);
        }
        await using var restored = DatabaseMigrationSafety.Openable(destination, SqliteOpenMode.ReadOnly);
        await restored.OpenAsync(cancellationToken);
        await DatabaseMigrationSafety.IntegrityAsync(restored, cancellationToken: cancellationToken);
        if (schema != await DatabaseMigrationSafety.SchemaAsync(restored, cancellationToken: cancellationToken))
            throw new InvalidOperationException("El esquema del respaldo no coincide con el origen.");
        DatabaseMigrationSafety.RequireSameData(data, await DatabaseMigrationSafety.DataAsync(restored, cancellationToken: cancellationToken));
        return new(destination, schema, data);
    }

    public static Task<DatabaseBackup> RestoreToNewFileAsync(string backup, string destination, CancellationToken cancellationToken = default) =>
        BackupAsync(backup, destination, cancellationToken);

    public static async Task<MigrationOutcome> ApplyAsync(string database, string backupDirectory, bool confirmStopped, bool allowCreate = false,
        CancellationToken cancellationToken = default, IInterceptor[]? testInterceptors = null)
    {
        if (!confirmStopped) throw new InvalidOperationException("Confirmá que detuviste todas las instancias y otros escritores antes de migrar.");
        database = DatabaseMigrationSafety.AbsolutePath(database); backupDirectory = DatabaseMigrationSafety.AbsolutePath(backupDirectory);
        RequirePrivateBackupPath(backupDirectory);
        if (string.Equals(database, backupDirectory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("El destino de respaldos debe ser un directorio diferente de la base.");
        if (!File.Exists(database) && !allowCreate) throw new InvalidOperationException("La base no existe. Solo --create explícito puede inicializarla.");
        if (allowCreate) Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        using var lease = DatabaseMigrationSafety.MaintenanceLease(database);
        var assessment = await DatabaseMigrationSafety.InspectAsync(database, cancellationToken);
        if (assessment.State is DatabaseSchemaState.Incompatible or DatabaseSchemaState.Locked)
            throw new InvalidOperationException(assessment.Detail);
        if (assessment.State is DatabaseSchemaState.Empty or DatabaseSchemaState.Missing && !allowCreate)
            throw new InvalidOperationException("Una base vacía requiere --create explícito.");
        Directory.CreateDirectory(backupDirectory);
        var operation = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        var receipt = Path.Combine(backupDirectory, operation + ".migration-result.json");
        DatabaseBackup? backup = null;
        if (assessment.State == DatabaseSchemaState.Current) {
            await WriteReceipt("AlreadyCurrent", null, assessment.Applied, assessment.Applied, null);
            return new("AlreadyCurrent", null, receipt);
        }
        if (File.Exists(database)) backup = await BackupAsync(database, Path.Combine(backupDirectory, operation + ".db"), cancellationToken);
        await WriteReceipt("Prepared", backup?.Path, assessment.Applied, assessment.Applied, null);
        try {
            // Reserve a new empty file only for an explicitly approved initialization. Every SQLite connection
            // then uses ReadWrite, so even a path disappearing mid-operation cannot create a replacement DB.
            if (assessment.State == DatabaseSchemaState.Missing) {
                using (new FileStream(database, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            }
            await using (var connection = DatabaseMigrationSafety.Openable(database, SqliteOpenMode.ReadWrite)) {
                await connection.OpenAsync(cancellationToken);
                var options = new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite(connection);
                if (testInterceptors is not null) options.AddInterceptors(testInterceptors);
                await using var db = new AquarellaDbContext(options.Options);
                await db.Database.MigrateAsync(cancellationToken);
            }
            await DatabaseMigrationSafety.RequireCurrentAsync(database, cancellationToken);
            if (backup is not null) {
                await using var check = DatabaseMigrationSafety.Openable(database, SqliteOpenMode.ReadOnly);
                await check.OpenAsync(cancellationToken);
                DatabaseMigrationSafety.RequireSameData(backup.Data, await DatabaseMigrationSafety.DataAsync(check, original: backup.Data, cancellationToken: cancellationToken));
            }
            await WriteReceipt("Completed", backup?.Path, assessment.Applied, DatabaseMigrationSafety.KnownMigrations(), null);
            return new("Completed", backup?.Path, receipt);
        }
        catch (Exception e) {
            // Never silently restore over newer data. Preserve the backup and failure receipt for the operator.
            var after = await DatabaseMigrationSafety.InspectAsync(database, CancellationToken.None);
            await WriteReceipt("Failed", backup?.Path, assessment.Applied, after.Applied, e.GetType().Name);
            throw new InvalidOperationException($"Migración detenida. Conservá el respaldo y revisá el registro de resultado: {receipt}");
        }
        Task WriteReceipt(string status, string? saved, string[] before, string[] after, string? failureType) =>
            File.WriteAllTextAsync(receipt, JsonSerializer.Serialize(new { Status = status, Database = database, Backup = saved, Before = before, After = after,
                FailureType = failureType, CreatedAtUtc = DateTime.UtcNow, OriginalRows = backup?.Data.Tables.ToDictionary(p => p.Key, p => p.Value.Rows) },
                new JsonSerializerOptions { WriteIndented = true }), CancellationToken.None);
    }
    private static void RequirePrivateBackupPath(string path)
    {
        if (path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p.Equals("wwwroot", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("No se permite guardar bases o respaldos dentro de wwwroot. Elegí un directorio privado.");
    }
}
