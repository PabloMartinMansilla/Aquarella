using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

namespace Aquarella.Services;

public static class ProductionHosting
{
    public static void Configure(WebApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment()) return;
        var config = builder.Configuration;
        if (string.Equals(config["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Usá la configuración explícita de ReverseProxy en lugar de habilitar todos los forwarded headers.");
        if (config["PORT"] is { } port)
        {
            if (!int.TryParse(port, out var number) || number is < 1 or > 65535)
                throw new InvalidOperationException("PORT debe ser un puerto TCP válido.");
            builder.WebHost.UseUrls($"http://0.0.0.0:{number}");
        }

        if (!Uri.TryCreate(config["Accounts:PublicOrigin"], UriKind.Absolute, out var origin)
            || origin.Scheme != "https" || origin.AbsolutePath != "/" || origin.Query.Length != 0
            || origin.Fragment.Length != 0 || origin.UserInfo.Length != 0)
            throw new InvalidOperationException("Production requiere Accounts:PublicOrigin con un origen HTTPS válido.");
        var hosts = (config["AllowedHosts"] ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (hosts.Length == 0 || hosts.Any(h => h.Contains('*')) || !hosts.Contains(origin.Host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Production requiere AllowedHosts explícito que incluya el hostname público.");

        var root = config["ProductionStorage:Root"];
        var keys = config["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)
            || string.IsNullOrWhiteSpace(keys) || !Inside(keys, root))
            throw new InvalidOperationException("Configurá ProductionStorage:Root y DataProtection:KeysPath dentro del volumen persistente.");
        if (Inside(root, builder.Environment.ContentRootPath)
            || SamePath(root, builder.Environment.ContentRootPath))
            throw new InvalidOperationException("El volumen de Production debe estar separado de los archivos de la aplicación.");
        if (!string.IsNullOrWhiteSpace(config["RAILWAY_SERVICE_ID"]) && string.IsNullOrWhiteSpace(config["RAILWAY_VOLUME_MOUNT_PATH"]))
            throw new InvalidOperationException("El servicio de Railway requiere un volumen persistente montado.");
        if (config["RAILWAY_VOLUME_MOUNT_PATH"] is { } mount
            && !SamePath(root, mount))
            throw new InvalidOperationException("ProductionStorage:Root debe coincidir con el volumen montado en Railway.");
        var connection = config.GetConnectionString("Aquarella");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Production requiere ConnectionStrings:Aquarella explícito.");
        var database = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connection).DataSource;
        if (!Inside(database, root) || SamePath(database, keys))
            throw new InvalidOperationException("La base de Production debe estar dentro del volumen persistente.");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(keys);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(keys, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        builder.Services.AddDataProtection().SetApplicationName("Aquarella")
            .PersistKeysToFileSystem(new DirectoryInfo(keys));

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Railway sets X-Real-IP, not X-Forwarded-For. Accept it only from configured proxies.
            options.ForwardedForHeaderName = "X-Real-IP";
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var item in config.GetSection("ReverseProxy:KnownProxies").GetChildren())
                options.KnownProxies.Add(IPAddress.Parse(item.Value!));
            foreach (var item in config.GetSection("ReverseProxy:KnownNetworks").GetChildren())
            {
                var network = System.Net.IPNetwork.Parse(item.Value!);
                if (network.PrefixLength == 0)
                    throw new InvalidOperationException("No se permite confiar en todas las redes del proxy.");
                options.KnownIPNetworks.Add(network);
            }
            // An empty trust list means trust everyone in ASP.NET Core. Refuse that configuration.
            if (options.KnownProxies.Count == 0 && options.KnownIPNetworks.Count == 0)
                throw new InvalidOperationException("Production requiere ReverseProxy:KnownProxies o KnownNetworks verificados.");
        });
    }

    private static bool SamePath(string first, string second) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool Inside(string path, string root)
    {
        if (!Path.IsPathFullyQualified(path)) return false;
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative != "." && !Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
