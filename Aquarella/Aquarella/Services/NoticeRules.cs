using Aquarella.Models;

namespace Aquarella.Services;

// Conditions derived from current data, never saved as separate notifications.
public static class NoticeRules
{
    public static IReadOnlyList<BusinessNotice> Evaluate(IEnumerable<NoticeProduct> products)
    {
        var notices = new List<BusinessNotice>();
        foreach (var item in products)
        {
            var product = item.Product;
            var price = item.Price;
            if (product.Quantity <= 0)
                notices.Add(new(NoticeKind.OutOfStock, NoticeSeverity.Critical, product.Id, product.Name));
            // Default zero-cost pricing is not evidence of loss or a measurable percentage.
            if (price.Cost <= 0) continue;
            if (price.RealProfit < 0)
                notices.Add(new(NoticeKind.SellingAtLoss, NoticeSeverity.Critical, product.Id, product.Name,
                    price.Cost, price.EffectiveSalePrice));
            else if (price.ProfitPercentOverCost is decimal percent && percent <= 1m)
                notices.Add(new(NoticeKind.CriticalProfit, NoticeSeverity.Critical, product.Id, product.Name,
                    ProfitPercent: percent));
        }
        return notices.OrderBy(n => n.Severity).ThenBy(n => n.Kind)
            .ThenBy(n => n.Kind == NoticeKind.SellingAtLoss ? n.SalePrice - n.Cost : n.ProfitPercent)
            .ThenBy(n => n.ProductName, StringComparer.CurrentCultureIgnoreCase).ThenBy(n => n.ProductId).ToArray();
    }
}
