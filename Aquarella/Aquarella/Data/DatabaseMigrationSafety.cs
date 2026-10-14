using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aquarella.Data;

public enum DatabaseSchemaState { Missing, Empty, Pending, Current, Incompatible, Locked }
public sealed record DatabaseAssessment(DatabaseSchemaState State, string[] Applied, string[] Pending, string Detail);
public sealed record TableDataStamp(string[] Columns, long Rows, string Digest);
public sealed record DatabaseDataStamp(SortedDictionary<string, TableDataStamp> Tables);

// No configuration fallback, database creation, schema repair, or migration occurs in this inspector.
public static class DatabaseMigrationSafety
{
    private static readonly ConcurrentDictionary<string, Task<string>> referenceSchemas = new();
    public static string[] KnownMigrations()
    {
        using var db = new AquarellaDbContext(new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite("Data Source=:memory:").Options);
        if (db.Database.HasPendingModelChanges()) throw new InvalidOperationException("El modelo tiene cambios sin migración versionada.");
        return db.Database.GetMigrations().ToArray();
    }
    public static string AbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("La herramienta requiere una ruta absoluta explícita.");
        var full = Path.GetFullPath(path);
        // A maintenance destination must not follow a symlink into an unintended database.
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current)) {
            FileSystemInfo item = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (item.Exists && item.LinkTarget is not null) throw new InvalidOperationException("No se admiten enlaces simbólicos en rutas de mantenimiento.");
        }
        return full;
    }
    public static SqliteConnection Openable(string path, SqliteOpenMode mode) => new(new SqliteConnectionStringBuilder {
        DataSource = AbsolutePath(path), Mode = mode, Pooling = false, ForeignKeys = true, DefaultTimeout = 5
    }.ToString());

    public static async Task<DatabaseAssessment> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        path = AbsolutePath(path);
        var known = KnownMigrations();
        if (!File.Exists(path)) return new(DatabaseSchemaState.Missing, [], known, "La base no existe; el arranque no la creará.");
        try {
            await using var connection = Openable(path, SqliteOpenMode.ReadOnly);
            await connection.OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction(deferred: true);
            await IntegrityAsync(connection, transaction, cancellationToken);
            var tables = await NamesAsync(connection, transaction, cancellationToken);
            if (tables.Contains("__EFMigrationsLock") && Convert.ToInt64(await Scalar(connection, "SELECT COUNT(*) FROM \"__EFMigrationsLock\"", transaction, cancellationToken)) != 0)
                return new(DatabaseSchemaState.Locked, [], [], "Hay un bloqueo EF de migración pendiente; requiere revisión, no se eliminará automáticamente.");
            var schema = await SchemaAsync(connection, transaction, cancellationToken);
            if (!tables.Contains("__EFMigrationsHistory"))
                return new(tables.Count == 0 ? DatabaseSchemaState.Empty : DatabaseSchemaState.Incompatible, [], known,
                    tables.Count == 0 ? "Base vacía; requiere inicialización explícita." : "Hay tablas sin historial EF reconocido. No se adoptará el esquema automáticamente.");
            var applied = new List<string>();
            await using (var command = connection.CreateCommand()) {
                command.Transaction = transaction; command.CommandText = "SELECT MigrationId FROM \"__EFMigrationsHistory\" ORDER BY MigrationId";
                await using var rows = await command.ExecuteReaderAsync(cancellationToken);
                while (await rows.ReadAsync(cancellationToken)) applied.Add(rows.GetString(0));
            }
            if (applied.Count == 0 || !applied.SequenceEqual(known.Take(applied.Count)))
                return new(DatabaseSchemaState.Incompatible, applied.ToArray(), [], "Historial desconocido, adelantado, incompleto o no consecutivo.");
            var expected = await referenceSchemas.GetOrAdd(applied[^1], BuildReferenceSchema).WaitAsync(cancellationToken);
            if (schema != expected) return new(DatabaseSchemaState.Incompatible, applied.ToArray(), [], "El esquema físico no coincide con su historial: revisar columnas, índices, restricciones o tablas manuales.");
            var pending = known.Skip(applied.Count).ToArray();
            return new(pending.Length == 0 ? DatabaseSchemaState.Current : DatabaseSchemaState.Pending, applied.ToArray(), pending,
                pending.Length == 0 ? "Esquema compatible y sin migraciones pendientes." : "Migraciones pendientes; la aplicación no las aplicará al iniciar.");
        }
        catch (SqliteException) { return new(DatabaseSchemaState.Incompatible, [], [], "No se pudo verificar la base SQLite en modo solo lectura."); }
        catch (InvalidOperationException) { return new(DatabaseSchemaState.Incompatible, [], [], "No se pudo verificar integridad, relaciones o esquema; requiere revisión."); }
    }

    public static async Task RequireCurrentAsync(string path, CancellationToken cancellationToken = default)
    {
        var assessment = await InspectAsync(path, cancellationToken);
        if (assessment.State != DatabaseSchemaState.Current)
            throw new InvalidOperationException($"Inicio detenido: {assessment.Detail} Usá tools/DatabaseMaintenance para validar, respaldar y migrar explícitamente con la aplicación detenida.");
    }

    public static FileStream ApplicationLease(string path, bool singleInstance = false) => Lease(path, exclusive: singleInstance);
    public static FileStream MaintenanceLease(string path) => Lease(path, exclusive: true);
    private static FileStream Lease(string path, bool exclusive)
    {
        path = AbsolutePath(path);
        if (!Directory.Exists(Path.GetDirectoryName(path))) throw new InvalidOperationException("El directorio de la base no existe; debe prepararse explícitamente.");
        var lockPath = AbsolutePath(path + ".maintenance.lock");
        try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, exclusive ? FileShare.None : FileShare.ReadWrite); }
        catch (IOException) { throw new InvalidOperationException("La aplicación o un mantenimiento ya utiliza la base. Detené las instancias antes de migrar."); }
    }

    private static async Task<string> BuildReferenceSchema(string migration)
    {
        // Versioned migrations are executed exclusively in a private in-memory reference, never in the inspected file.
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AquarellaDbContext(new DbContextOptionsBuilder<AquarellaDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync(migration);
        return await SchemaAsync(connection, null, CancellationToken.None);
    }
    public static async Task IntegrityAsync(SqliteConnection connection, SqliteTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(Convert.ToString(await Scalar(connection, "PRAGMA integrity_check", transaction, cancellationToken)), "ok", StringComparison.Ordinal))
            throw new InvalidOperationException("La base no supera integrity_check.");
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "PRAGMA foreign_key_check";
        await using var rows = await command.ExecuteReaderAsync(cancellationToken);
        if (await rows.ReadAsync(cancellationToken)) throw new InvalidOperationException("La base presenta relaciones inválidas; no se repararán automáticamente.");
    }
    public static async Task<string> SchemaAsync(SqliteConnection connection, SqliteTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT type,name,tbl_name,sql FROM sqlite_schema WHERE name NOT GLOB 'sqlite_*' AND name != '__EFMigrationsLock' ORDER BY type,name";
        await using var rows = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<string[]>();
        while (await rows.ReadAsync(cancellationToken)) result.Add([rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.IsDBNull(3) ? "" :
            Regex.Replace(rows.GetString(3), "'(?:''|[^'])*'|\"(?:\"\"|[^\"])*\"|\\s+", m => char.IsWhiteSpace(m.Value[0]) ? " " : m.Value).Trim()]);
        return JsonSerializer.Serialize(result);
    }
    private static async Task<List<string>> NamesAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT GLOB 'sqlite_*' ORDER BY name";
        await using var rows = await command.ExecuteReaderAsync(cancellationToken); var result = new List<string>();
        while (await rows.ReadAsync(cancellationToken)) result.Add(rows.GetString(0)); return result;
    }
    public static async Task<DatabaseDataStamp> DataAsync(SqliteConnection connection, SqliteTransaction? transaction = null, DatabaseDataStamp? original = null, CancellationToken cancellationToken = default)
    {
        var result = new SortedDictionary<string, TableDataStamp>(StringComparer.Ordinal);
        var tables = original is null ? (await NamesAsync(connection, transaction, cancellationToken)).Where(t => !t.StartsWith("__EF", StringComparison.Ordinal)) : original.Tables.Keys;
        foreach (var table in tables) {
            var columns = original?.Tables[table].Columns;
            if (columns is null) {
                var names = new List<string>(); await using var describe = connection.CreateCommand();
                describe.Transaction = transaction; describe.CommandText = $"PRAGMA table_info({Quote(table)})";
                await using var rows = await describe.ExecuteReaderAsync(cancellationToken);
                while (await rows.ReadAsync(cancellationToken)) names.Add(rows.GetString(1)); columns = names.ToArray();
            }
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long count = 0;
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            var selected = string.Join(",", columns.Select(Quote)); command.CommandText = $"SELECT {selected} FROM {Quote(table)} ORDER BY {selected}";
            await using var values = await command.ExecuteReaderAsync(cancellationToken);
            while (await values.ReadAsync(cancellationToken)) {
                var row = new object?[columns.Length];
                for (var i = 0; i < row.Length; i++) row[i] = values.IsDBNull(i) ? null : values.GetValue(i);
                hash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(row) + "\n")); count++;
            }
            result.Add(table, new(columns, count, Convert.ToHexString(hash.GetHashAndReset())));
        }
        return new(result);
    }
    public static void RequireSameData(DatabaseDataStamp before, DatabaseDataStamp after)
    {
        if (JsonSerializer.Serialize(before) != JsonSerializer.Serialize(after)) throw new InvalidOperationException("Los datos anteriores no coinciden; la base no debe utilizarse hasta revisar/restaurar el respaldo.");
    }
    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    private static async Task<object?> Scalar(SqliteConnection connection, string sql, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; return await command.ExecuteScalarAsync(cancellationToken);
    }
}
