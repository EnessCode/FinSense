using FinSense.Application.Common.Interfaces;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;

namespace FinSense.Application.Features.Investments.Contracts;

public sealed record InvestmentLotDto(
    Guid Id,
    decimal Quantity,
    decimal UnitPrice,
    decimal TotalCost,
    DateTime PurchaseDateUtc,
    string? Notes);

public sealed record InvestmentHoldingDto(
    string Symbol,
    string AssetName,
    InvestmentAssetCategory Category,
    decimal TotalQuantity,
    decimal AverageUnitCost,
    decimal TotalCost,
    decimal CurrentUnitPrice,
    decimal CurrentValue,
    decimal ProfitLossAmount,
    decimal ProfitLossPercent,
    string Currency,
    IReadOnlyList<InvestmentLotDto> Lots);

public sealed record InvestmentCategoryAllocationDto(
    InvestmentAssetCategory Category,
    decimal TotalCost,
    decimal CurrentValue,
    decimal SharePercent);

public sealed record InvestmentPortfolioDto(
    decimal TotalInvestedCost,
    decimal TotalCurrentValue,
    decimal TotalProfitLossAmount,
    decimal TotalProfitLossPercent,
    IReadOnlyList<InvestmentCategoryAllocationDto> CategoryAllocation,
    IReadOnlyList<InvestmentHoldingDto> Holdings);

public sealed record UpdateAssetPriceResultDto(
    string Symbol,
    decimal CurrentUnitPrice,
    int UpdatedLotCount);

internal static class InvestmentPortfolioCalculator
{
    public static async Task<IReadOnlyDictionary<(InvestmentAssetCategory Category, string Symbol), decimal>> FetchLivePricesAsync(
        IMarketPriceService? marketPriceService,
        IReadOnlyList<InvestmentTransaction> transactions,
        CancellationToken cancellationToken)
    {
        if (marketPriceService is null || transactions.Count == 0)
        {
            return new Dictionary<(InvestmentAssetCategory, string), decimal>();
        }

        var distinctAssets = transactions
            .Select(t => (t.Category, t.Symbol))
            .Distinct()
            .ToList();

        var tasks = distinctAssets.Select(async asset =>
        {
            try
            {
                var price = await marketPriceService.GetLatestPriceTryAsync(
                    asset.Category,
                    asset.Symbol,
                    cancellationToken);

                return (Asset: asset, Price: price);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return (Asset: asset, Price: (decimal?)null);
            }
        });

        var results = await Task.WhenAll(tasks);

        return results
            .Where(r => r.Price.HasValue && r.Price.Value > 0m)
            .ToDictionary(r => r.Asset, r => r.Price!.Value);
    }

    public static InvestmentPortfolioDto BuildPortfolio(
        IReadOnlyList<InvestmentTransaction> transactions,
        IReadOnlyDictionary<(InvestmentAssetCategory Category, string Symbol), decimal>? livePrices = null)
    {
        if (transactions.Count == 0)
        {
            return new InvestmentPortfolioDto(0m, 0m, 0m, 0m, [], []);
        }

        var holdings = transactions
            .GroupBy(t => new { t.Category, t.Symbol })
            .Select(group =>
            {
                var orderedLots = group
                    .OrderBy(t => t.PurchaseDateUtc)
                    .ThenBy(t => t.CreatedAtUtc)
                    .ToList();

                var latestLot = orderedLots
                    .OrderByDescending(t => t.CreatedAtUtc)
                    .First();

                var totalQuantity = orderedLots.Sum(l => l.Quantity);
                var totalCost = decimal.Round(orderedLots.Sum(l => l.TotalCost), 2, MidpointRounding.AwayFromZero);
                var currentUnitPrice =
                    livePrices is not null &&
                    livePrices.TryGetValue((group.Key.Category, group.Key.Symbol), out var livePrice) &&
                    livePrice > 0m
                        ? decimal.Round(livePrice, 4, MidpointRounding.AwayFromZero)
                        : latestLot.CurrentUnitPrice;
                var averageUnitCost = totalQuantity > 0m
                    ? decimal.Round(totalCost / totalQuantity, 4, MidpointRounding.AwayFromZero)
                    : 0m;
                var currentValue = decimal.Round(totalQuantity * currentUnitPrice, 2, MidpointRounding.AwayFromZero);
                var profitLossAmount = decimal.Round(currentValue - totalCost, 2, MidpointRounding.AwayFromZero);
                var profitLossPercent = totalCost > 0m
                    ? decimal.Round((profitLossAmount / totalCost) * 100m, 2, MidpointRounding.AwayFromZero)
                    : 0m;

                var lotsDto = orderedLots
                    .Select(l => new InvestmentLotDto(
                        l.Id,
                        l.Quantity,
                        l.UnitPrice,
                        l.TotalCost,
                        l.PurchaseDateUtc,
                        l.Notes))
                    .ToList();

                return new InvestmentHoldingDto(
                    group.Key.Symbol,
                    latestLot.AssetName,
                    group.Key.Category,
                    totalQuantity,
                    averageUnitCost,
                    totalCost,
                    currentUnitPrice,
                    currentValue,
                    profitLossAmount,
                    profitLossPercent,
                    latestLot.Currency,
                    lotsDto);
            })
            .OrderByDescending(h => h.CurrentValue)
            .ThenBy(h => h.Symbol)
            .ToList();

        var totalInvestedCost = decimal.Round(holdings.Sum(h => h.TotalCost), 2, MidpointRounding.AwayFromZero);
        var totalCurrentValue = decimal.Round(holdings.Sum(h => h.CurrentValue), 2, MidpointRounding.AwayFromZero);
        var totalProfitLossAmount = decimal.Round(totalCurrentValue - totalInvestedCost, 2, MidpointRounding.AwayFromZero);
        var totalProfitLossPercent = totalInvestedCost > 0m
            ? decimal.Round((totalProfitLossAmount / totalInvestedCost) * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var categoryAllocation = holdings
            .GroupBy(h => h.Category)
            .Select(g =>
            {
                var catTotalCost = decimal.Round(g.Sum(x => x.TotalCost), 2, MidpointRounding.AwayFromZero);
                var catCurrentValue = decimal.Round(g.Sum(x => x.CurrentValue), 2, MidpointRounding.AwayFromZero);
                var sharePercent = totalCurrentValue > 0m
                    ? decimal.Round((catCurrentValue / totalCurrentValue) * 100m, 2, MidpointRounding.AwayFromZero)
                    : 0m;

                return new InvestmentCategoryAllocationDto(
                    g.Key,
                    catTotalCost,
                    catCurrentValue,
                    sharePercent);
            })
            .OrderByDescending(c => c.CurrentValue)
            .ToList();

        return new InvestmentPortfolioDto(
            totalInvestedCost,
            totalCurrentValue,
            totalProfitLossAmount,
            totalProfitLossPercent,
            categoryAllocation,
            holdings);
    }
}
