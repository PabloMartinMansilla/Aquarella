using Aquarella.Models;
using Microsoft.JSInterop;

namespace Aquarella.Services;

// Shared saved state for Agenda, persisted per browser like the business profile.
public sealed class AgendaStore(IJSRuntime js) : IAsyncDisposable
{
    private List<AgendaNote> notes = [];
    private IJSObjectReference? storage, watcher;
    private DotNetObjectReference<AgendaStore>? receiver;
    private Task? initialization;
    private readonly SemaphoreSlim gate = new(1, 1);
    public event Action? Changed;
    public IReadOnlyList<AgendaNote> Notes => notes.AsReadOnly();
    public Task InitializeAsync() => initialization ??= Initialize();
    private async Task Initialize()
    {
        storage = await js.InvokeAsync<IJSObjectReference>("import", "./agenda-storage.js");
        notes = await storage.InvokeAsync<List<AgendaNote>>("load");
        receiver = DotNetObjectReference.Create(this);
        watcher = await storage.InvokeAsync<IJSObjectReference>("watch", receiver);
        Changed?.Invoke();
    }
    [JSInvokable] public async Task Reload()
    {
        await gate.WaitAsync();
        try { notes = await storage!.InvokeAsync<List<AgendaNote>>("load"); Changed?.Invoke(); }
        finally { gate.Release(); }
    }
    public async Task SaveAsync(AgendaNote note)
    {
        if ((string.IsNullOrWhiteSpace(note.Title) && string.IsNullOrWhiteSpace(note.Content)) || note.Title.Length > 120 || note.Content.Length > 4000
            || (note.Time is not null && !System.Text.RegularExpressions.Regex.IsMatch(note.Time, "^([01][0-9]|2[0-3]):[0-5][0-9]$"))) throw new ArgumentException("Nota inválida.");
        await Mutate(next => { next.RemoveAll(n => n.Id == note.Id); next.Add(note); });
    }
    public Task DeleteAsync(Guid id) => Mutate(next => next.RemoveAll(n => n.Id == id));
    private async Task Mutate(Action<List<AgendaNote>> change)
    {
        await gate.WaitAsync();
        try {
            // Read the latest browser snapshot to preserve notes saved in another tab.
            var next = await storage!.InvokeAsync<List<AgendaNote>>("load");
            change(next);
            await storage!.InvokeVoidAsync("save", next);
            notes = next;
            Changed?.Invoke();
        } finally { gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        try { if (watcher is not null) { await watcher.InvokeVoidAsync("dispose"); await watcher.DisposeAsync(); } if (storage is not null) await storage.DisposeAsync(); }
        catch (JSDisconnectedException) { }
        receiver?.Dispose();
    }
}


