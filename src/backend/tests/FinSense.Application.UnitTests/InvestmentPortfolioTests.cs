using System.Net;
using System.Text;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Features.Analytics.Queries.GetAnalyticsSummary;
using FinSense.Application.Features.Investments.Commands;
using FinSense.Application.Features.Investments.Queries.GetInvestmentPortfolio;
using FinSense.Application.UnitTests.Common;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinSense.Application.UnitTests;

public class InvestmentPortfolioTests
{
    [Fact]
    public async Task AddMultipleLots_And_UpdateCurrentPrice_ShouldAggregateHoldings_ComputePnL_AndLeaveBankWalletsUntouched()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider
        {
            UtcNow = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc)
        };

        var user = User.Create("invest@finsense.app", "hash", "Investor", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);
        var bankWallet = Wallet.Create(workspace.Id, "Maaş Hesabı", WalletType.BankAccount, initialBalance: 100000m);

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        dbContext.Wallets.Add(bankWallet);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id, Locale = "tr-TR" };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);

        var addHandler = new AddInvestmentLotCommandHandler(dbContext, accessService, dateTimeProvider);
        var priceHandler = new UpdateAssetCurrentPriceCommandHandler(dbContext, accessService, dateTimeProvider);
        var portfolioHandler = new GetInvestmentPortfolioQueryHandler(dbContext, accessService);
        var analyticsHandler = new GetAnalyticsSummaryQueryHandler(dbContext, accessService, currentUser);

        var firstDate = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        var secondDate = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

        // 1. Add 10 grams GAU at 3,000 TRY = 30,000 TRY
        var lot1 = await addHandler.Handle(
            new AddInvestmentLotCommand(
                workspace.Id,
                InvestmentAssetCategory.Gold,
                "gau",
                "Gram Altın",
                Quantity: 10m,
                UnitPrice: 3000m,
                TotalCost: null,
                CurrentUnitPrice: null,
                PurchaseDateUtc: firstDate,
                Notes: "İlk altın alımı"),
            CancellationToken.None);

        dateTimeProvider.UtcNow = dateTimeProvider.UtcNow.AddMinutes(5);

        // 2. Add 10 grams GAU at 3,400 TRY = 34,000 TRY
        var lot2 = await addHandler.Handle(
            new AddInvestmentLotCommand(
                workspace.Id,
                InvestmentAssetCategory.Gold,
                "GAU",
                "Gram Altın",
                Quantity: 10m,
                UnitPrice: 3400m,
                TotalCost: null,
                CurrentUnitPrice: null,
                PurchaseDateUtc: secondDate,
                Notes: "İkinci altın alımı"),
            CancellationToken.None);

        var initialPortfolio = await portfolioHandler.Handle(
            new GetInvestmentPortfolioQuery(workspace.Id),
            CancellationToken.None);

        initialPortfolio.Holdings.Should().ContainSingle();
        var goldHolding = initialPortfolio.Holdings.Single();
        goldHolding.Symbol.Should().Be("GAU");
        goldHolding.AssetName.Should().Be("Gram Altın");
        goldHolding.Category.Should().Be(InvestmentAssetCategory.Gold);
        goldHolding.TotalQuantity.Should().Be(20m);
        goldHolding.AverageUnitCost.Should().Be(3200m);
        goldHolding.TotalCost.Should().Be(64000m);
        goldHolding.Lots.Should().HaveCount(2);
        goldHolding.Lots[0].Id.Should().Be(lot1.Id);
        goldHolding.Lots[0].PurchaseDateUtc.Should().Be(firstDate);
        goldHolding.Lots[0].TotalCost.Should().Be(30000m);
        goldHolding.Lots[1].Id.Should().Be(lot2.Id);
        goldHolding.Lots[1].PurchaseDateUtc.Should().Be(secondDate);
        goldHolding.Lots[1].TotalCost.Should().Be(34000m);

        // 3. Update live currentUnitPrice for GAU to 3,500 TRY -> CurrentValue = 70,000, PnL = +6,000 (+9.38%)
        var priceUpdate = await priceHandler.Handle(
            new UpdateAssetCurrentPriceCommand(workspace.Id, "gau", 3500m),
            CancellationToken.None);

        priceUpdate.Symbol.Should().Be("GAU");
        priceUpdate.CurrentUnitPrice.Should().Be(3500m);
        priceUpdate.UpdatedLotCount.Should().Be(2);

        var updatedPortfolio = await portfolioHandler.Handle(
            new GetInvestmentPortfolioQuery(workspace.Id),
            CancellationToken.None);

        updatedPortfolio.TotalInvestedCost.Should().Be(64000m);
        updatedPortfolio.TotalCurrentValue.Should().Be(70000m);
        updatedPortfolio.TotalProfitLossAmount.Should().Be(6000m);
        updatedPortfolio.TotalProfitLossPercent.Should().Be(9.38m);

        var updatedHolding = updatedPortfolio.Holdings.Single();
        updatedHolding.CurrentUnitPrice.Should().Be(3500m);
        updatedHolding.CurrentValue.Should().Be(70000m);
        updatedHolding.ProfitLossAmount.Should().Be(6000m);
        updatedHolding.ProfitLossPercent.Should().Be(9.38m);

        updatedPortfolio.CategoryAllocation.Should().ContainSingle(c =>
            c.Category == InvestmentAssetCategory.Gold &&
            c.TotalCost == 64000m &&
            c.CurrentValue == 70000m &&
            c.SharePercent == 100m);

        // 4. Verify regular bank wallet & monthly expense analytics are completely untouched
        var walletInDb = await dbContext.Wallets.AsNoTracking().SingleAsync(w => w.Id == bankWallet.Id);
        walletInDb.CurrentBalance.Should().Be(100000m);

        var analytics = await analyticsHandler.Handle(
            new GetAnalyticsSummaryQuery(workspace.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 30)),
            CancellationToken.None);
        analytics.TotalExpense.Should().Be(0m);
    }

    [Fact]
    public async Task DeleteInvestmentLot_ShouldRemoveOnlyTargetLot_AndRecalculateRemainingHoldings()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var user = User.Create("invest2@finsense.app", "hash", "Investor 2", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);

        var addHandler = new AddInvestmentLotCommandHandler(dbContext, accessService, dateTimeProvider);
        var deleteHandler = new DeleteInvestmentLotCommandHandler(dbContext, accessService, dateTimeProvider);
        var portfolioHandler = new GetInvestmentPortfolioQueryHandler(dbContext, accessService);

        var btcLot = await addHandler.Handle(
            new AddInvestmentLotCommand(
                workspace.Id,
                InvestmentAssetCategory.Crypto,
                "BTC",
                "Bitcoin",
                Quantity: 0.025m,
                UnitPrice: 2800000m,
                CurrentUnitPrice: 3000000m),
            CancellationToken.None);

        var thyLot = await addHandler.Handle(
            new AddInvestmentLotCommand(
                workspace.Id,
                InvestmentAssetCategory.Bist,
                "THYAO",
                "Türk Hava Yolları",
                Quantity: 100m,
                UnitPrice: 280m,
                CurrentUnitPrice: 300m),
            CancellationToken.None);

        await deleteHandler.Handle(new DeleteInvestmentLotCommand(workspace.Id, btcLot.Id), CancellationToken.None);

        var portfolio = await portfolioHandler.Handle(new GetInvestmentPortfolioQuery(workspace.Id), CancellationToken.None);
        portfolio.Holdings.Should().ContainSingle(h => h.Symbol == "THYAO" && h.Lots.Single().Id == thyLot.Id);
        portfolio.TotalInvestedCost.Should().Be(28000m);
        portfolio.TotalCurrentValue.Should().Be(30000m);
        portfolio.TotalProfitLossAmount.Should().Be(2000m);
    }

    [Fact]
    public async Task AddInvestmentLotCommand_ShouldAlwaysCalculateTotalCostDeterministicallyOnServer_IgnoringManualClientTotalCost()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var user = User.Create("cost@finsense.app", "hash", "Cost Investor", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);
        var addHandler = new AddInvestmentLotCommandHandler(dbContext, accessService, dateTimeProvider);

        // Quantity: 12.345678, UnitPrice: 123.4567 -> 12.345678 * 123.4567 = 1524.1565573426 -> Round 2 = 1524.16
        // Client manually passes TotalCost = 999999.99m (which must be completely ignored by the server)
        var lot = await addHandler.Handle(
            new AddInvestmentLotCommand(
                workspace.Id,
                InvestmentAssetCategory.Crypto,
                "ETH",
                "Ethereum",
                Quantity: 12.345678m,
                UnitPrice: 123.4567m,
                TotalCost: 999999.99m),
            CancellationToken.None);

        var expectedTotalCost = Math.Round(12.345678m * 123.4567m, 2, MidpointRounding.AwayFromZero);
        expectedTotalCost.Should().Be(1524.16m);

        lot.TotalCost.Should().Be(1524.16m);
        lot.TotalCost.Should().NotBe(999999.99m);

        var storedInDb = await dbContext.InvestmentTransactions.SingleAsync(l => l.Id == lot.Id);
        storedInDb.TotalCost.Should().Be(1524.16m);
    }

    private sealed class FakeMarketPriceService : IMarketPriceService
    {
        private readonly Dictionary<(InvestmentAssetCategory Category, string Symbol), decimal?> _prices = new();
        public bool ShouldThrow { get; set; }

        public void SetPrice(InvestmentAssetCategory category, string symbol, decimal? priceTry) =>
            _prices[(category, symbol.Trim().ToUpperInvariant())] = priceTry;

        public Task<decimal?> GetLatestPriceTryAsync(
            InvestmentAssetCategory category,
            string symbol,
            CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
            {
                throw new HttpRequestException("Simulated upstream failure");
            }

            return Task.FromResult(
                _prices.TryGetValue((category, symbol.Trim().ToUpperInvariant()), out var price)
                    ? price
                    : null);
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public int CallCount { get; private set; }
        public List<string> RequestedUrls { get; } = [];

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (request.RequestUri is not null)
            {
                RequestedUrls.Add(request.RequestUri.ToString());
            }

            return Task.FromResult(_responder(request));
        }
    }

    [Fact]
    public async Task GetInvestmentPortfolioQuery_WithLiveMarketPricesExceedingCostBasis_ShouldComputeRealTimePnL()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var user = User.Create("live@finsense.app", "hash", "Live Investor", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);
        var addHandler = new AddInvestmentLotCommandHandler(dbContext, accessService, dateTimeProvider);

        // THYAO: 100 shares @ 250 TRY = 25,000 TRY cost
        await addHandler.Handle(
            new AddInvestmentLotCommand(
                workspace.Id, InvestmentAssetCategory.Bist, "THYAO", "Türk Hava Yolları", 100m, 250m),
            CancellationToken.None);

        // GAU: 10 grams @ 3,000 TRY = 30,000 TRY cost
        await addHandler.Handle(
            new AddInvestmentLotCommand(
                workspace.Id, InvestmentAssetCategory.Gold, "GAU", "Gram Altın", 10m, 3000m),
            CancellationToken.None);

        // Live market prices exceed average cost basis: THYAO -> 310 TRY (+24%), GAU -> 3,300 TRY (+10%)
        var fakeMarket = new FakeMarketPriceService();
        fakeMarket.SetPrice(InvestmentAssetCategory.Bist, "THYAO", 310m);
        fakeMarket.SetPrice(InvestmentAssetCategory.Gold, "GAU", 3300m);

        var portfolioHandler = new GetInvestmentPortfolioQueryHandler(dbContext, accessService, fakeMarket);
        var portfolio = await portfolioHandler.Handle(new GetInvestmentPortfolioQuery(workspace.Id), CancellationToken.None);

        portfolio.TotalInvestedCost.Should().Be(55000m);
        // 100*310 (31,000) + 10*3300 (33,000) = 64,000
        portfolio.TotalCurrentValue.Should().Be(64000m);
        portfolio.TotalProfitLossAmount.Should().Be(9000m);
        // (9000 / 55000) * 100 = 16.3636... -> 16.36%
        portfolio.TotalProfitLossPercent.Should().Be(16.36m);

        var thyHolding = portfolio.Holdings.Single(h => h.Symbol == "THYAO");
        thyHolding.CurrentUnitPrice.Should().Be(310m);
        thyHolding.CurrentValue.Should().Be(31000m);
        thyHolding.ProfitLossAmount.Should().Be(6000m);
        thyHolding.ProfitLossPercent.Should().Be(24.00m);

        var gauHolding = portfolio.Holdings.Single(h => h.Symbol == "GAU");
        gauHolding.CurrentUnitPrice.Should().Be(3300m);
        gauHolding.CurrentValue.Should().Be(33000m);
        gauHolding.ProfitLossAmount.Should().Be(3000m);
        gauHolding.ProfitLossPercent.Should().Be(10.00m);

        // Graceful fallback: when external market price service throws, stored prices (250 / 3000) are used cleanly
        fakeMarket.ShouldThrow = true;
        var fallbackPortfolio = await portfolioHandler.Handle(
            new GetInvestmentPortfolioQuery(workspace.Id),
            CancellationToken.None);

        fallbackPortfolio.TotalCurrentValue.Should().Be(55000m);
        fallbackPortfolio.TotalProfitLossAmount.Should().Be(0m);
    }

    [Fact]
    public async Task YahooBinanceMarketPriceService_ShouldFetchBistCryptoGold_CacheResults_AndHandleFailuresGracefully()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            var url = req.RequestUri!.ToString();

            if (url.Contains("THYAO.IS"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"chart":{"result":[{"meta":{"regularMarketPrice":312.50}}]}}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (url.Contains("symbol=BTCTRY"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"symbol":"BTCTRY","price":"3150000.00"}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (url.Contains("USDTRY%3DX") || url.Contains("USDTRY=X"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"chart":{"result":[{"meta":{"regularMarketPrice":35.00}}]}}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (url.Contains("GC%3DF") || url.Contains("GC=F"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"chart":{"result":[{"meta":{"regularMarketPrice":2650.00}}]}}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var httpClient = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new YahooBinanceMarketPriceService(
            httpClient,
            cache,
            NullLogger<YahooBinanceMarketPriceService>.Instance);

        // 1. BIST lowercase normalization + 5-min caching
        var thyPrice1 = await service.GetLatestPriceTryAsync(InvestmentAssetCategory.Bist, "thyao");
        var thyPrice2 = await service.GetLatestPriceTryAsync(InvestmentAssetCategory.Bist, "THYAO");
        thyPrice1.Should().Be(312.50m);
        thyPrice2.Should().Be(312.50m);
        handler.RequestedUrls.Count(u => u.Contains("THYAO.IS")).Should().Be(1);

        // 2. Crypto Binance TRY pair
        var btcPrice = await service.GetLatestPriceTryAsync(InvestmentAssetCategory.Crypto, "btc");
        btcPrice.Should().Be(3150000m);

        // 3. Gold GAU formula: (2650 * 35.00) / 31.1035 = 2981.9795
        var gauPrice = await service.GetLatestPriceTryAsync(InvestmentAssetCategory.Gold, "GAU");
        gauPrice.Should().Be(decimal.Round((2650m * 35.00m) / 31.1035m, 4, MidpointRounding.AwayFromZero));

        // 4. Unknown/failing ticker returns null gracefully
        var unknownPrice = await service.GetLatestPriceTryAsync(InvestmentAssetCategory.Bist, "UNKNOWN999");
        unknownPrice.Should().BeNull();
    }
}
