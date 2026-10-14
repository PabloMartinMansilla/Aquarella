using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

namespace Aquarella.Services;

public static class ProductionHosting
{
    private sealed record RailwayProxyPolicy(string PublicHost);

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

        // Validate the trust boundary before creating any storage or starting the server.
        var proxies = config.GetSection("ReverseProxy:KnownProxies").GetChildren().Select(item =>
            IPAddress.TryParse(item.Value, out var address) && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any)
                ? address : throw new InvalidOperationException("ReverseProxy:KnownProxies requiere direcciones IP explícitas válidas.")).ToArray();
        var networks = config.GetSection("ReverseProxy:KnownNetworks").GetChildren().Select(item =>
            System.Net.IPNetwork.TryParse(item.Value, out var network) && network.PrefixLength > 0
                ? network : throw new InvalidOperationException("ReverseProxy:KnownNetworks requiere rangos verificados; no se permite confiar en todas las redes.")).ToArray();
        var railwayDedicated = config.GetValue<bool>("ReverseProxy:RailwayDedicatedEnvironmentEnabled");
        if (railwayDedicated)
        {
            if (!builder.Environment.IsProduction()
                || new[] { "RAILWAY_PROJECT_ID", "RAILWAY_ENVIRONMENT_ID", "RAILWAY_SERVICE_ID", "RAILWAY_VOLUME_MOUNT_PATH", "PORT" }
                    .Any(key => string.IsNullOrWhiteSpace(config[key])))
                throw new InvalidOperationException("El modo Railway dedicado requiere Production y las variables de servicio, entorno, proyecto, volumen y puerto de Railway.");
            if (proxies.Length != 0 || networks.Length != 0)
                throw new InvalidOperationException("No mezcles el modo Railway dedicado con KnownProxies/KnownNetworks.");
            if (new[] { "RAILWAY_TCP_PROXY_DOMAIN", "RAILWAY_TCP_PROXY_PORT", "RAILWAY_TCP_APPLICATION_PORT" }
                .Any(key => !string.IsNullOrWhiteSpace(config[key])))
                throw new InvalidOperationException("El modo Railway dedicado no permite un TCP proxy público del contenedor.");
            var domain = config["RAILWAY_PUBLIC_DOMAIN"];
            if (string.IsNullOrWhiteSpace(domain) || Uri.CheckHostName(domain) != UriHostNameType.Dns
                || !domain.EndsWith(".up.railway.app", StringComparison.OrdinalIgnoreCase)
                || !origin.IsDefaultPort || !domain.Equals(origin.Host, StringComparison.OrdinalIgnoreCase)
                || hosts.Any(host => !host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                    && !host.Equals("healthcheck.railway.app", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("El modo Railway dedicado requiere el dominio público gratuito de Railway como origen HTTPS y AllowedHosts limitado a ese dominio y healthcheck.railway.app.");
        }
        else if (proxies.Length == 0 && networks.Length == 0)
            throw new InvalidOperationException("Production requiere ReverseProxy:KnownProxies o KnownNetworks verificados.");

        var root = config["ProductionStorage:Root"];
        var keys = config["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) || SamePath(root, Path.GetPathRoot(root)!)
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
        if (!Inside(database, root) || SamePath(database, keys) || Inside(database, keys))
            throw new InvalidOperationException("La base de Production debe estar dentro del volumen persistente.");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(keys);
        if (!OperatingSystem.IsWindows()) {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(keys, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        builder.Services.AddDataProtection().SetApplicationName("Aquarella")
            .PersistKeysToFileSystem(new DirectoryInfo(keys));

        if (railwayDedicated)
        {
            builder.Services.AddSingleton(new RailwayProxyPolicy(origin.Host));
            return;
        }
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Consume X-Real-IP only from explicitly configured proxies in this mode.
            options.ForwardedForHeaderName = "X-Real-IP";
            options.ForwardLimit = 1;
            options.RequireHeaderSymmetry = true;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in proxies) options.KnownProxies.Add(proxy);
            foreach (var network in networks) options.KnownIPNetworks.Add(network);
        });
    }

    public static void UseProxy(WebApplication app)
    {
        if (app.Environment.IsDevelopment()) return;
        var railway = app.Services.GetService<RailwayProxyPolicy>();
        if (railway is null)
        {
            app.UseForwardedHeaders();
            return;
        }

        // Explicitly authorized perimeter, NOT authentication of the edge. Any reachable
        // peer can imitate it. Never add another service to this environment without review.
        app.Logger.LogWarning("Modo de proxy Railway dedicado activo: se confía en el perímetro del entorno. Agregar servicios o exponer el puerto interno requiere una nueva revisión de seguridad.");
        app.Use(async (context, next) =>
        {
            var host = context.Request.Host.Value;
            var publicHost = string.Equals(host, railway.PublicHost, StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, railway.PublicHost + ":443", StringComparison.OrdinalIgnoreCase);
            var healthHost = string.Equals(host, "healthcheck.railway.app", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "healthcheck.railway.app:" + context.Connection.LocalPort.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
            if (context.Request.Path == "/health" && HttpMethods.IsGet(context.Request.Method)
                && (publicHost || healthHost))
            {
                await next();
                return;
            }

            var proto = context.Request.Headers["X-Forwarded-Proto"];
            var realIp = context.Request.Headers["X-Real-IP"];
            if (!publicHost || proto.Count != 1 || proto[0] != "https" || realIp.Count != 1
                || !TryClientIp(realIp[0], out var address))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("Solicitud de proxy no válida.");
                return;
            }

            // Only these two fields are consumed. Host, path and all other forwarded
            // headers remain untouched; there is no fallback to X-Forwarded-Host/For.
            context.Request.Scheme = "https";
            context.Connection.RemoteIpAddress = address;
            await next();
        });
    }

    private static bool TryClientIp(string? value, out IPAddress? address)
    {
        address = null;
        if (string.IsNullOrEmpty(value) || value.Length > 45
            || value.Any(c => char.IsWhiteSpace(c) || c is ',' or '%' or '[' or ']')
            || !IPAddress.TryParse(value, out var parsed)) return false;
        // Reject non-standard IPv4 forms accepted by TryParse (integer, hex, shortened).
        if (parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            && parsed.ToString() != value) return false;
        if (parsed.IsIPv4MappedToIPv6) parsed = parsed.MapToIPv4();
        if (parsed.Equals(IPAddress.Any) || parsed.Equals(IPAddress.IPv6Any) || parsed.IsIPv6Multicast
            || parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && parsed.GetAddressBytes()[0] >= 224)
            return false;
        address = parsed;
        return true;
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
