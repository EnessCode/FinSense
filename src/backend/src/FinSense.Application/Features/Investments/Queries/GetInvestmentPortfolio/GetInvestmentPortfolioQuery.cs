using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Investments.Contracts;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Investments.Queries.GetInvestmentPortfolio;

public sealed record GetInvestmentPortfolioQuery(Guid WorkspaceId) : IRequest<InvestmentPortfolioDto>;

public sealed class GetInvestmentPortfolioQueryHandler
    : IRequestHandler<GetInvestmentPortfolioQuery, InvestmentPortfolioDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly IMarketPriceService? _marketPriceService;

    public GetInvestmentPortfolioQueryHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        IMarketPriceService? marketPriceService = null)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _marketPriceService = marketPriceService;
    }

    public async Task<InvestmentPortfolioDto> Handle(
        GetInvestmentPortfolioQuery request,
        CancellationToken cancellationToken)
    {
        await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Viewer,
            cancellationToken);

        var transactions = await _dbContext.InvestmentTransactions
            .AsNoTracking()
            .Where(i => i.WorkspaceId == request.WorkspaceId)
            .OrderBy(i => i.PurchaseDateUtc)
            .ThenBy(i => i.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var livePrices = await InvestmentPortfolioCalculator.FetchLivePricesAsync(
            _marketPriceService,
            transactions,
            cancellationToken);

        return InvestmentPortfolioCalculator.BuildPortfolio(transactions, livePrices);
    }
}
