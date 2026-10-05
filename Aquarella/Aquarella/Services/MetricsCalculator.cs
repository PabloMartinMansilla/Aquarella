using Aquarella.Models;

namespace Aquarella.Services;

public static class MetricsCalculator
{
    public static BusinessMetrics Calculate(IReadOnlyList<NoticeProduct> products)
    {
        long units = 0;
        int outOfStock = 0, inStock = 0, withCost = 0, withSale = 0;
        decimal costValue = 0, saleValue = 0, measuredSaleValue = 0;
        var profits = new List<ProductProfitMetric>();
        foreach (var item in products)
        {
            var product = item.Product; var price = item.Price;
            units += product.Quantity;
            if (product.Quantity <= 0) outOfStock++;
            else
            {
                inStock++;
                // A zero default cost cannot distinguish a missing cost from a free acquisition.
                if (price.Cost > 0)
                {
                    withCost++;
                    costValue += price.Cost * product.Quantity;
                    measuredSaleValue += price.EffectiveSalePrice * product.Quantity;
                }
                // Explicit manual zero is a real price; default zero with no cost is not configured.
                if (price.ManualSalePrice || price.Cost > 0)
                {
                    withSale++;
                    saleValue += price.EffectiveSalePrice * product.Quantity;
                }
            }
            if (price.ProfitPercentOverCost is decimal percentage)
                profits.Add(new(product.Id, product.Name, price.Cost, price.EffectiveSalePrice, price.RealProfit, percentage));
        }
        var ordered = profits.OrderBy(p => p.ProfitPercent).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Id).ToArray();
        var notices = NoticeRules.Evaluate(products).GroupBy(n => new { n.Kind, n.Severity })
            .Select(group => new NoticeMetric(group.Key.Kind, group.Key.Severity, group.Count())).ToArray();
        return new(products.Count, units, outOfStock, inStock, withCost, withSale,
            inStock == 0 || withCost > 0 ? costValue : null,
            inStock == 0 || withSale > 0 ? saleValue : null,
            costValue > 0 ? (measuredSaleValue - costValue) / costValue * 100 : null,
            profits.Count, ordered.Take(5).ToArray(),
            ordered.OrderByDescending(p => p.ProfitPercent).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.Id).Take(5).ToArray(), notices);
    }
}
