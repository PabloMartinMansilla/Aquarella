namespace Aquarella.Components;

public static class ModuleCatalog
{
    public static IReadOnlyList<Module> All { get; } =
    [
        new("Stock", "/stock", "Productos e inventario", "stock", "M4 7h16v14H4z M3 3h18v4H3z M9 11h6"),
        new("Avisos", "/avisos", "Novedades de tu negocio", "notices", "M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9 M10 21h4"),
        new("Conversaciones", "/conversaciones", "El espacio de tus mensajes", "conversations", "M21 11a9 9 0 0 1-9 9H4l-2 2V11a9 9 0 0 1 19 0Z M7 10h10 M7 14h6"),
        new("Configuración de IA", "/configuracion-ia", "Tu asistente, a tu manera", "ai", "M12 3v3 M9 3h6 M5 7h14v13H5z M2 11h3 M19 11h3 M8 12h1 M15 12h1 M9 16h6"),
        new("Métricas", "/metricas", "Una mirada a tus resultados", "metrics", "M4 3v18h17 M8 16v-5 M13 16V7 M18 16V4"),
        new("Agenda / Reservas", "/agenda", "Organizá tus próximos encuentros", "agenda", "M4 5h16v16H4z M8 2v6 M16 2v6 M4 10h16 M8 14h2 M14 14h2 M8 17h2"),
        new("Perfil", "/perfil", "Identidad y datos del negocio", "profile", "M16 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0 M4 21v-2a8 8 0 0 1 16 0v2"),
        new("Precios", "/precios", "Costos, precios y ganancias", "prices", "M3 3h9l9 9-9 9-9-9z M7 7h.01 M10 10l6 6")
    ];

    public sealed record Module(string Title, string Route, string Description, string Accent, string IconPath);
}


