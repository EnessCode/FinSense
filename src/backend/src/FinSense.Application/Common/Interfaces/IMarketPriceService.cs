using FinSense.Domain.Enums;

namespace FinSense.Application.Common.Interfaces;

public interface IMarketPriceService
{
    /// <summary>
    /// Fetches the latest market price in TRY for the specified asset category and symbol.
    /// Returns <c>null</c> when the symbol is unsupported or external market data is unavailable.
    /// </summary>
    Task<decimal?> GetLatestPriceTryAsync(
        InvestmentAssetCategory category,
        string symbol,
        CancellationToken cancellationToken = default);
}
