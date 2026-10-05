namespace Aquarella.Services;
public static class PersistenceErrors
{
    public static bool IsStorageError(Exception e) => e is Microsoft.JSInterop.JSException or Microsoft.EntityFrameworkCore.DbUpdateException or Microsoft.Data.Sqlite.SqliteException or System.Text.Json.JsonException or System.ComponentModel.DataAnnotations.ValidationException or InvalidOperationException or ArgumentException;
}

