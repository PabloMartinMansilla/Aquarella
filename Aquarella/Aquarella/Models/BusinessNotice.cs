namespace Aquarella.Models;

public enum NoticeSeverity { Critical, Attention }
public enum NoticeKind { SellingAtLoss, OutOfStock, CriticalProfit }
public sealed record NoticeProduct(StockProduct Product, ProductPrice Price);
public sealed record BusinessNotice(NoticeKind Kind, NoticeSeverity Severity, Guid ProductId,
    string ProductName, decimal? Cost = null, decimal? SalePrice = null, decimal? ProfitPercent = null);
