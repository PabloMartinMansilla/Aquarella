using Aquarella.Models;

namespace Aquarella.Services;

// One scoped projection of current products: no historical guesses, external calls or stored metrics.
public sealed class MetricsService(BusinessData data)
{
    public async Task<BusinessMetrics> LoadAsync() => MetricsCalculator.Calculate(await data.LoadNoticeProductsAsync());
}
