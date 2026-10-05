namespace Aquarella.Models;

public sealed record ProductProfitMetric(Guid Id, string Name, decimal Cost, decimal SalePrice,
    decimal Profit, decimal ProfitPercent);
public sealed record NoticeMetric(NoticeKind Kind, NoticeSeverity Severity, int Count);
public sealed record BusinessMetrics(int ProductCount, long Units, int OutOfStock,
    int ProductsInStock, int ProductsWithCost, int ProductsWithSalePrice,
    decimal? InventoryCost, decimal? InventorySaleValue, decimal? WeightedProfitPercent,
    int ProductsWithProfit, IReadOnlyList<ProductProfitMetric> LowestProfit,
    IReadOnlyList<ProductProfitMetric> HighestProfit, IReadOnlyList<NoticeMetric> Notices)
{
    public int ActiveNotices => Notices.Sum(n => n.Count);
    public int ProductsSellingAtLoss => Notices.Where(n => n.Kind == NoticeKind.SellingAtLoss).Sum(n => n.Count);
    public int ProductsWithCriticalProfit => Notices.Where(n => n.Kind == NoticeKind.CriticalProfit).Sum(n => n.Count);
}
