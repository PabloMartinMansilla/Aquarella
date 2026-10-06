using Aquarella.Models;
namespace Aquarella.Services;
public sealed class AgendaStore(BusinessData data, ILogger<AgendaStore> logger) : IDisposable
{
    private List<AgendaNote> notes = [];
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool subscribed, disposed;
    public event Action? Changed;
    public IReadOnlyList<AgendaNote> Notes => notes.AsReadOnly();
    public async Task InitializeAsync() {
        if (disposed) return;
        if (!subscribed) { data.NotesChanged += Reload; subscribed = true; }
        await gate.WaitAsync();
        try { var loaded = await data.LoadNotesAsync(); if (!disposed) { notes = loaded; Changed?.Invoke(); } } finally { gate.Release(); }
    }
    private void Reload() { if (!disposed) _ = ReloadSafely(); }
    private async Task ReloadSafely() { try { await InitializeAsync(); } catch (Exception e) { logger.LogWarning(e, "Agenda refresh failed."); } }
    public async Task SaveAsync(AgendaNote note) { await data.SaveNoteAsync(note); await InitializeAsync(); }
    public async Task DeleteAsync(Guid id) { await data.DeleteNoteAsync(id); await InitializeAsync(); }
    public void Dispose() { disposed = true; if (subscribed) data.NotesChanged -= Reload; }
}
