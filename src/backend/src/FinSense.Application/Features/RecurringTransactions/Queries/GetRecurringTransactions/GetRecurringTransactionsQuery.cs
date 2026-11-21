using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Categories.Contracts;
using FinSense.Application.Features.RecurringTransactions.Contracts;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.RecurringTransactions.Queries.GetRecurringTransactions;

public sealed record GetRecurringTransactionsQuery(Guid WorkspaceId)
    : IRequest<RecurringTransactionsOverviewDto>;

public sealed class GetRecurringTransactionsQueryHandler
    : IRequestHandler<GetRecurringTransactionsQuery, RecurringTransactionsOverviewDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly ICurrentUserService _currentUserService;
    private readonly IRecurringTransactionProcessor _recurringProcessor;

    public GetRecurringTransactionsQueryHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        ICurrentUserService currentUserService,
        IRecurringTransactionProcessor recurringProcessor)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _currentUserService = currentUserService;
        _recurringProcessor = recurringProcessor;
    }

    public async Task<RecurringTransactionsOverviewDto> Handle(
        GetRecurringTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Viewer,
            cancellationToken);

        // Process any due recurring bills/incomes before returning the list
        await _recurringProcessor.ProcessDueTransactionsAsync(request.WorkspaceId, cancellationToken);

        var isEnglish = CategoryLocalization.IsEnglish(_currentUserService.Locale);

        var items = await _dbContext.RecurringTransactions
            .AsNoTracking()
            .Where(r => r.WorkspaceId == request.WorkspaceId)
            .OrderByDescending(r => r.IsActive)
            .ThenBy(r => r.NextExecutionDateUtc)
            .ThenBy(r => r.Title)
            .Select(r => new RecurringTransactionDto(
                r.Id,
                r.WalletId,
                r.Wallet.Name,
                r.CategoryId,
                isEnglish && r.Category.NameEn != null ? r.Category.NameEn : r.Category.NameTr,
                r.Type,
                r.Amount,
                r.Currency,
                r.Title,
                r.DayOfMonth,
                r.TotalOccurrences,
                r.CompletedOccurrences,
                r.TotalOccurrences.HasValue
                    ? Math.Max(0, r.TotalOccurrences.Value - r.CompletedOccurrences)
                    : null,
                r.NextExecutionDateUtc,
                r.LastExecutedAtUtc,
                r.IsActive))
            .ToListAsync(cancellationToken);

        var activeItems = items.Where(i => i.IsActive).ToList();
        var totalMonthlyFixedIncome = activeItems
            .Where(i => i.Type == TransactionType.Income)
            .Sum(i => i.Amount);
        var totalMonthlyFixedExpense = activeItems
            .Where(i => i.Type == TransactionType.Expense)
            .Sum(i => i.Amount);
        var netMonthlyFixedFlow = totalMonthlyFixedIncome - totalMonthlyFixedExpense;

        return new RecurringTransactionsOverviewDto(
            items,
            totalMonthlyFixedIncome,
            totalMonthlyFixedExpense,
            netMonthlyFixedFlow);
    }
}
