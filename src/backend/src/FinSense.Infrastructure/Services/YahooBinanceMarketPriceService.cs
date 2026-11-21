using System.Globalization;
using System.Text.Json;
using FinSense.Application.Common.Interfaces;
using FinSense.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FinSense.Infrastructure.Services;

public sealed class YahooBinanceMarketPriceService : IMarketPriceService
{
    private const decimal TroyOunceToGrams = 31.1035m;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<YahooBinanceMarketPriceService> _logger;

    public YahooBinanceMarketPriceService(
        HttpClient httpClient,
        IMemoryCache memoryCache,
        ILogger<YahooBinanceMarketPriceService> logger)
    {
        _httpClient = httpClient;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async Task<decimal?> GetLatestPriceTryAsync(
        InvestmentAssetCategory category,
        string symbol,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        var cacheKey = $"market_price:{(short)category}:{normalizedSymbol}";

        if (_memoryCache.TryGetValue(cacheKey, out decimal cachedPrice) && cachedPrice > 0m)
        {
            return cachedPrice;
        }

        try
        {
            decimal? fetchedPrice = category switch
            {
                InvestmentAssetCategory.Bist => await FetchBistPriceTryAsync(normalizedSymbol, cancellationToken),
                InvestmentAssetCategory.Crypto => await FetchCryptoPriceTryAsync(normalizedSymbol, cancellationToken),
                InvestmentAssetCategory.Gold => await FetchGoldPriceTryAsync(normalizedSymbol, cancellationToken),
                InvestmentAssetCategory.Forex => await FetchForexPriceTryAsync(normalizedSymbol, cancellationToken),
                InvestmentAssetCategory.Fund => null,
                _ => null
            };

            if (fetchedPrice.HasValue && fetchedPrice.Value > 0m)
            {
                var rounded = decimal.Round(fetchedPrice.Value, 4, MidpointRounding.AwayFromZero);
                _memoryCache.Set(cacheKey, rounded, CacheDuration);
                return rounded;
            }

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to fetch live market price for {Category}/{Symbol}. Falling back to stored price.",
                category,
                normalizedSymbol);
            return null;
        }
    }

    private async Task<decimal?> FetchBistPriceTryAsync(string symbol, CancellationToken cancellationToken)
    {
        var ticker = symbol.EndsWith(".IS", StringComparison.OrdinalIgnoreCase)
            ? symbol
            : $"{symbol}.IS";

        return await FetchYahooChartPriceAsync(ticker, cancellationToken);
    }

    private async Task<decimal?> FetchCryptoPriceTryAsync(string symbol, CancellationToken cancellationToken)
    {
        // 1. Direct Binance TRY pair (e.g., BTCTRY, ETHTRY, SOLTRY)
        var directTry = await FetchBinanceTickerPriceAsync($"{symbol}TRY", cancellationToken);
        if (directTry.HasValue && directTry.Value > 0m)
        {
            return directTry.Value;
        }

        // 2. Fallback via USDT pair converted to TRY
        var usdtPrice = await FetchBinanceTickerPriceAsync($"{symbol}USDT", cancellationToken);
        if (!usdtPrice.HasValue || usdtPrice.Value <= 0m)
        {
            return null;
        }

        var usdtTry = await FetchBinanceTickerPriceAsync("USDTTRY", cancellationToken)
                      ?? await FetchForexPriceTryAsync("USD", cancellationToken);

        if (!usdtTry.HasValue || usdtTry.Value <= 0m)
        {
            return null;
        }

        return usdtPrice.Value * usdtTry.Value;
    }

    private async Task<decimal?> FetchGoldPriceTryAsync(string symbol, CancellationToken cancellationToken)
    {
        // Primary: (Gold Ounce USD * USDTRY) / 31.1035
        var usdTry = await FetchForexPriceTryAsync("USD", cancellationToken);
        decimal? gramTry = null;

        if (usdTry.HasValue && usdTry.Value > 0m)
        {
            var goldOunceUsd = await FetchYahooChartPriceAsync("GC=F", cancellationToken)
                               ?? await FetchYahooChartPriceAsync("XAUUSD=X", cancellationToken);

            if (goldOunceUsd.HasValue && goldOunceUsd.Value > 0m)
            {
                if (symbol is "ONS" or "XAUUSD")
                {
                    return goldOunceUsd.Value * usdTry.Value;
                }

                gramTry = (goldOunceUsd.Value * usdTry.Value) / TroyOunceToGrams;
            }
        }

        // Fallback via Binance tokenized physical gold (PAXGTRY = 1 troy ounce gold in TRY)
        if (!gramTry.HasValue)
        {
            var paxgTry = await FetchBinanceTickerPriceAsync("PAXGTRY", cancellationToken);
            if (!paxgTry.HasValue && usdTry.HasValue && usdTry.Value > 0m)
            {
                var paxgUsdt = await FetchBinanceTickerPriceAsync("PAXGUSDT", cancellationToken);
                if (paxgUsdt.HasValue && paxgUsdt.Value > 0m)
                {
                    paxgTry = paxgUsdt.Value * usdTry.Value;
                }
            }

            if (paxgTry.HasValue && paxgTry.Value > 0m)
            {
                if (symbol is "ONS" or "XAUUSD")
                {
                    return paxgTry.Value;
                }

                gramTry = paxgTry.Value / TroyOunceToGrams;
            }
        }

        if (!gramTry.HasValue || gramTry.Value <= 0m)
        {
            return null;
        }

        // Standard Turkish gold mint pure-gold multipliers (22K = 0.91667 purity)
        return symbol switch
        {
            "CEYREK" => gramTry.Value * 1.6042m,
            "YARIM" => gramTry.Value * 3.2083m,
            "TAM" or "CUMHURIYET" => gramTry.Value * 6.4167m,
            "ATA" => gramTry.Value * 6.6147m,
            _ => gramTry.Value // GAU, GRAM, XAU, etc.
        };
    }

    private async Task<decimal?> FetchForexPriceTryAsync(string symbol, CancellationToken cancellationToken)
    {
        if (symbol == "TRY")
        {
            return 1m;
        }

        var cacheKey = $"market_price:fx:{symbol}";
        if (_memoryCache.TryGetValue(cacheKey, out decimal cachedFx) && cachedFx > 0m)
        {
            return cachedFx;
        }

        var yahooFx = await FetchYahooChartPriceAsync($"{symbol}TRY=X", cancellationToken);
        if (yahooFx.HasValue && yahooFx.Value > 0m)
        {
            _memoryCache.Set(cacheKey, yahooFx.Value, CacheDuration);
            return yahooFx.Value;
        }

        var openErFx = await FetchOpenExchangeRateTryAsync(symbol, cancellationToken);
        if (openErFx.HasValue && openErFx.Value > 0m)
        {
            _memoryCache.Set(cacheKey, openErFx.Value, CacheDuration);
            return openErFx.Value;
        }

        return null;
    }

    private async Task<decimal?> FetchYahooChartPriceAsync(string yahooSymbol, CancellationToken cancellationToken)
    {
        var encodedSymbol = Uri.EscapeDataString(yahooSymbol);
        string[] hosts =
        [
            $"https://query1.finance.yahoo.com/v8/finance/chart/{encodedSymbol}?interval=1d&range=1d",
            $"https://query2.finance.yahoo.com/v8/finance/chart/{encodedSymbol}?interval=1d&range=1d"
        ];

        foreach (var url in hosts)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                if (!doc.RootElement.TryGetProperty("chart", out var chart) ||
                    !chart.TryGetProperty("result", out var results) ||
                    results.ValueKind != JsonValueKind.Array ||
                    results.GetArrayLength() == 0)
                {
                    continue;
                }

                var firstResult = results[0];
                if (!firstResult.TryGetProperty("meta", out var meta))
                {
                    continue;
                }

                if (TryReadDecimalProperty(meta, "regularMarketPrice", out var regularPrice) ||
                    TryReadDecimalProperty(meta, "chartPreviousClose", out regularPrice) ||
                    TryReadDecimalProperty(meta, "previousClose", out regularPrice))
                {
                    if (regularPrice > 0m)
                    {
                        return regularPrice;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Yahoo Finance lookup failed for {YahooSymbol} at {Url}", yahooSymbol, url);
            }
        }

        return null;
    }

    private async Task<decimal?> FetchBinanceTickerPriceAsync(string pair, CancellationToken cancellationToken)
    {
        var url = $"https://api.binance.com/api/v3/ticker/price?symbol={Uri.EscapeDataString(pair)}";
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (doc.RootElement.TryGetProperty("price", out var priceProp) &&
                TryParseDecimalElement(priceProp, out var price) &&
                price > 0m)
            {
                return price;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Binance ticker lookup failed for {Pair}", pair);
        }

        return null;
    }

    private async Task<decimal?> FetchOpenExchangeRateTryAsync(string currencyCode, CancellationToken cancellationToken)
    {
        var url = $"https://open.er-api.com/v6/latest/{Uri.EscapeDataString(currencyCode)}";
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (doc.RootElement.TryGetProperty("rates", out var rates) &&
                TryReadDecimalProperty(rates, "TRY", out var rate) &&
                rate > 0m)
            {
                return rate;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Open ER API lookup failed for {CurrencyCode}", currencyCode);
        }

        return null;
    }

    private static bool TryReadDecimalProperty(JsonElement element, string propertyName, out decimal value)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            return TryParseDecimalElement(prop, out value);
        }

        value = 0m;
        return false;
    }

    private static bool TryParseDecimalElement(JsonElement prop, out decimal value)
    {
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out value))
        {
            return true;
        }

        if (prop.ValueKind == JsonValueKind.String &&
            decimal.TryParse(prop.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0m;
        return false;
    }
}
