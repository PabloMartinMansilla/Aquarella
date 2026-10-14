namespace Aquarella.Services.Accounts;

// Bounded durable queue: one sender per worker, lease claims prevent overlapping workers.
// Password commits never depend on this worker succeeding; no raw link is stored in SQLite.
public sealed class AccountEmailWorker(IServiceScopeFactory scopes, ILogger<AccountEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!stoppingToken.IsCancellationRequested) {
            try {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AccountService>().ProcessPendingEmailsAsync(stoppingToken);
            }
            catch (Exception e) { logger.LogWarning("No se pudo procesar la cola de correo ({Type}).", e.GetType().Name); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
