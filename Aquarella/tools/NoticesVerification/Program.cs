using Aquarella.Models;
using Aquarella.Services;

var checks = 0;
NoticeProduct Item(int stock = 20, decimal cost = 100, decimal sale = 125, bool manual = true, decimal desired = 25) =>
    new(new(Guid.NewGuid(), "Producto de prueba", stock), new() { Cost = cost, SalePrice = sale, ManualSalePrice = manual, DesiredProfitPercent = desired });
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
Check(NoticeRules.Evaluate([Item()]).Count == 0, "Normal stock and profit");
Check(NoticeRules.Evaluate([Item(stock: 1)]).Count == 0, "No arbitrary low stock threshold");
Check(NoticeRules.Evaluate([Item(stock: 0)]).Single().Kind == NoticeKind.OutOfStock, "Zero stock");
Check(NoticeRules.Evaluate([Item(stock: -1)]).Single().Kind == NoticeKind.OutOfStock, "Nonpositive stock");
Check(NoticeRules.Evaluate([Item(sale: 100.8m)]).Single().Kind == NoticeKind.CriticalProfit, "0.8 percent profit");
Check(NoticeRules.Evaluate([Item(sale: 101m)]).Single().Kind == NoticeKind.CriticalProfit, "Inclusive 1 percent threshold");
Check(NoticeRules.Evaluate([Item(sale: 101.01m)]).Count == 0, "Unrounded percentage above threshold");
Check(NoticeRules.Evaluate([Item(sale: 100m)]).Single().Kind == NoticeKind.CriticalProfit, "Break even");
Check(NoticeRules.Evaluate([Item(sale: 90m)]).Single().Kind == NoticeKind.SellingAtLoss, "Loss without duplicate profit warning");
Check(NoticeRules.Evaluate([Item(cost: 0, sale: 0)]).Count == 0, "Default unconfigured pricing is not evidence of loss");
Check(NoticeRules.Evaluate([Item(sale: 0, manual: false)]).Count == 0, "Suggested price takes precedence over unused sale value");
Check(NoticeRules.Evaluate([Item(manual: false, desired: .8m)]).Single().Kind == NoticeKind.CriticalProfit, "Automatic suggested price uses existing rounding");
var corrected = Item(stock: 0, sale: 90m);
Check(NoticeRules.Evaluate([corrected]).Count == 2, "Independent stock and pricing problems");
corrected = corrected with { Product = corrected.Product with { Quantity = 20 } };
Check(NoticeRules.Evaluate([corrected]).Single().Kind == NoticeKind.SellingAtLoss, "Stock correction removes stock problem");
corrected.Price.SalePrice = 125m;
Check(NoticeRules.Evaluate([corrected]).Count == 0, "Price correction removes price problem");
var grouped = NoticeRules.Evaluate([Item(stock: 0), Item(stock: 0), Item(sale: 100), Item(sale: 90)]);
Check(grouped.GroupBy(n => n.Kind).Count() == 3 && grouped[0].Kind == NoticeKind.SellingAtLoss, "Grouping and economic loss priority");
Check(NoticeRules.Evaluate([]).Count == 0, "Empty business");
Console.WriteLine($"{checks} comprobaciones de Avisos correctas.");
