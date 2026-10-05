namespace Aquarella.Models;

public sealed class ProductPrice
{
    public Guid ProductId { get; set; }
    public decimal Cost { get; set; }
    public decimal DesiredProfitPercent { get; set; }
    public decimal SalePrice { get; set; }
    public bool ManualSalePrice { get; set; }
    public decimal SuggestedPrice => decimal.Round(Cost * (1 + DesiredProfitPercent / 100), 2, MidpointRounding.AwayFromZero);
    public decimal EffectiveSalePrice => ManualSalePrice ? SalePrice : SuggestedPrice;
    public decimal RealProfit => EffectiveSalePrice - Cost;
    public decimal? ProfitPercentOverCost => Cost > 0 ? RealProfit / Cost * 100 : null;
}
