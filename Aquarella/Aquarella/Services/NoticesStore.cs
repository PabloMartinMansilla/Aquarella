using Aquarella.Models;

namespace Aquarella.Services;

public sealed class NoticesStore : IDisposable
{
    private readonly BusinessData data;
    private readonly ILogger<NoticesStore> logger;
    private Task? refresh;
    private bool pending, disposed;
    public NoticesStore(BusinessData data, ILogger<NoticesStore> logger)
    {
        this.data = data; this.logger = logger;
        data.NoticesChanged += DataChanged;
    }
    public event Action? Changed;
    public IReadOnlyList<BusinessNotice> Items { get; private set; } = [];
    public bool Ready { get; private set; }
    public string? Error { get; private set; }
    public Task InitializeAsync() => Ready ? Task.CompletedTask : RefreshAsync();
    public Task RefreshAsync()
    {
        pending = true;
        return refresh is { IsCompleted: false } ? refresh : refresh = ReloadAsync();
    }
    private async Task ReloadAsync()
    {
        do
        {
            pending = false;
            try { Items = NoticeRules.Evaluate(await data.LoadNoticeProductsAsync()); Ready = true; Error = null; }
            catch (Exception e) when (PersistenceErrors.IsStorageError(e))
            {
                Ready = false; Error = "No se pudieron actualizar los avisos. Intentá nuevamente.";
                logger.LogWarning(e, "Notice refresh failed.");
            }
            if (!disposed) Changed?.Invoke();
        } while (pending && !disposed);
    }
    private void DataChanged() { if (!disposed) _ = RefreshAsync(); }
    public void Dispose() { disposed = true; data.NoticesChanged -= DataChanged; }
}
