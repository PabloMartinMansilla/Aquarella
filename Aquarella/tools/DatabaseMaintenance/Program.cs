using Aquarella.Data;
using System.Text.Json;

if (args.Length == 0 || args[0] == "--help") {
    Console.WriteLine("Aquarella DB: inspect --database <absolute.db> | inspect-private-beta --database <absolute.db> | backup --database <absolute.db> --destination <new-backup.db> | migrate --database <absolute.db> --backup-dir <private-directory> --confirm-migrate --confirm-stopped [--create] | restore --database <backup.db> --destination <new-restored.db>");
    return;
}
try {
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    var flags = new HashSet<string>(StringComparer.Ordinal);
    for (var i = 1; i < args.Length; i++) {
        if (args[i] is "--confirm-migrate" or "--confirm-stopped" or "--create") {
            if (!flags.Add(args[i])) throw new ArgumentException("Opción repetida.");
        } else if (args[i] is "--database" or "--destination" or "--backup-dir") {
            var key = args[i];
            if (++i == args.Length || args[i].StartsWith("--") || !values.TryAdd(key, args[i])) throw new ArgumentException("Falta un valor o hay una opción repetida.");
        } else throw new ArgumentException("Opción desconocida.");
    }
    var database = values.GetValueOrDefault("--database") ?? throw new ArgumentException("Falta --database; no se usa appsettings ni una ruta por defecto.");
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    switch (args[0]) {
        case "inspect-private-beta":
            if (flags.Count != 0 || values.Count != 1) throw new ArgumentException("inspect-private-beta solo admite --database.");
            var readiness = await PrivateBetaReadiness.InspectAsync(database, timeout.Token);
            Console.WriteLine(JsonSerializer.Serialize(readiness));
            if (!readiness.Ready) Environment.ExitCode = 2;
            break;
        case "inspect":
            var assessment = await DatabaseMigrationSafety.InspectAsync(database, timeout.Token);
            Console.WriteLine(JsonSerializer.Serialize(new { State = assessment.State.ToString(), assessment.Applied, assessment.Pending, assessment.Detail }));
            if (assessment.State is DatabaseSchemaState.Incompatible or DatabaseSchemaState.Locked) Environment.ExitCode = 2;
            break;
        case "backup":
        case "restore":
            var destination = values.GetValueOrDefault("--destination") ?? throw new ArgumentException("Falta --destination; nunca se sobrescribe un archivo existente.");
            var saved = await DatabaseMaintenance.BackupAsync(database, destination, timeout.Token);
            Console.WriteLine("Respaldo/restauración nueva verificada: " + saved.Path);
            break;
        case "migrate":
            if (!flags.Contains("--confirm-migrate")) throw new InvalidOperationException("Falta --confirm-migrate. Revisá primero inspect y la migración.");
            var outcome = await DatabaseMaintenance.ApplyAsync(database,
                values.GetValueOrDefault("--backup-dir") ?? throw new ArgumentException("Falta --backup-dir."), flags.Contains("--confirm-stopped"), flags.Contains("--create"), timeout.Token);
            Console.WriteLine(JsonSerializer.Serialize(outcome));
            break;
        default: throw new ArgumentException("Operación desconocida.");
    }
} catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or OperationCanceledException or Microsoft.Data.Sqlite.SqliteException) {
    // Never print SQL, connection strings, row values, or provider exception details.
    Console.Error.WriteLine(e is ArgumentException or InvalidOperationException ? e.Message : "Mantenimiento detenido: " + e.GetType().Name);
    Environment.ExitCode = 1;
}
