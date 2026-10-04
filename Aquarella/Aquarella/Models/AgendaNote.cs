namespace Aquarella.Models;

public sealed record AgendaNote(Guid Id, DateOnly Date, string Title, string Content, string? Time);
