using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Analytics.Contracts;
using FinSense.Application.Features.Categories.Contracts;
using FinSense.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Analytics.Queries.GetAnalyticsSummary;

public sealed record GetAnalyticsSummaryQuery(
    Guid WorkspaceId,
    DateOnly DateFrom,
    DateOnly DateTo) : IRequest<AnalyticsSummaryDto>;

public sealed class GetAnalyticsSummaryQueryValidator : AbstractValidator<GetAnalyticsSummaryQuery>
{
    public GetAnalyticsSummaryQueryValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();

        RuleFor(x => x)
            .Must(x => x.DateFrom <= x.DateTo)
            .WithMessage("Başlangıç tarihi bitiş tarihinden büyük olamaz.");
    }
}

public sealed class GetAnalyticsSummaryQueryHandler
    : IRequestHandler<GetAnalyticsSummaryQuery, AnalyticsSummaryDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly ICurrentUserService _currentUserService;
    private readonly IRecurringTransactionProcessor? _recurringProcessor;

    public GetAnalyticsSummaryQueryHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        ICurrentUserService currentUserService,
        IRecurringTransactionProcessor? recurringProcessor = null)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _currentUserService = currentUserService;
        _recurringProcessor = recurringProcessor;
    }

    public async Task<AnalyticsSummaryDto> Handle(
        GetAnalyticsSummaryQuery request,
        CancellationToken cancellationToken)
    {
        await _workspaceAccess.EnsureAccessAsync(request.WorkspaceId, WorkspaceRole.Viewer, cancellationToken);

        if (_recurringProcessor is not null)
        {
            await _recurringProcessor.ProcessDueTransactionsAsync(request.WorkspaceId, cancellationToken);
        }

        var isEnglish = CategoryLocalization.IsEnglish(_currentUserService.Locale);

        var currencyCode = await _dbContext.Workspaces
            .AsNoTracking()
            .Where(w => w.Id == request.WorkspaceId)
            .Select(w => w.DefaultCurrency)
            .FirstAsync(cancellationToken);

        // Non-Double-Counting Rule: strictly exclude TransactionType.Transfer (type = 3)
        var baseTransactions = _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.WorkspaceId == request.WorkspaceId &&
                        !t.IsDeleted &&
                        t.Type != TransactionType.Transfer &&
                        t.TransactionDate >= request.DateFrom &&
                        t.TransactionDate <= request.DateTo);

        var totalsByType = await baseTransactions
            .GroupBy(t => t.Type)
            .Select(g => new { Type = g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync(cancellationToken);

        var totalIncome = totalsByType.FirstOrDefault(x => x.Type == TransactionType.Income)?.Total ?? 0m;
        var totalExpense = totalsByType.FirstOrDefault(x => x.Type == TransactionType.Expense)?.Total ?? 0m;
        var netCashFlow = decimal.Round(totalIncome - totalExpense, 4, MidpointRounding.AwayFromZero);

        var savingsRatePercent = totalIncome > 0m
            ? decimal.Round((netCashFlow / totalIncome) * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var categoryGroups = await baseTransactions
            .Where(t => t.Type == TransactionType.Expense && t.CategoryId != null)
            .GroupBy(t => new
            {
                CategoryId = t.CategoryId!.Value,
                t.Category!.NameTr,
                t.Category.NameEn,
                t.Category.Icon,
                t.Category.ColorHex
            })
            .Select(g => new
            {
                g.Key.CategoryId,
                g.Key.NameTr,
                g.Key.NameEn,
                g.Key.Icon,
                g.Key.ColorHex,
                Amount = g.Sum(t => t.Amount),
                TransactionCount = g.Count()
            })
            .OrderByDescending(x => x.Amount)
            .ToListAsync(cancellationToken);

        var categoryBreakdown = categoryGroups
            .Select(c => new CategoryBreakdownItemDto(
                c.CategoryId,
                isEnglish && c.NameEn != null ? c.NameEn : c.NameTr,
                c.Icon,
                c.ColorHex,
                c.Amount,
                totalExpense > 0m
                    ? decimal.Round((c.Amount / totalExpense) * 100m, 2, MidpointRounding.AwayFromZero)
                    : 0m,
                c.TransactionCount))
            .ToList();

        var memberRows = await baseTransactions
            .Select(t => new
            {
                EffectiveUserId = t.PaidByUserId ?? t.CreatedByUserId,
                DisplayName = t.PaidByUser != null
                    ? t.PaidByUser.DisplayName
                    : (t.CreatedByUser != null ? t.CreatedByUser.DisplayName : null),
                t.Type,
                t.Amount
            })
            .ToListAsync(cancellationToken);

        var unknownMemberLabel = isEnglish ? "Former Member" : "Eski Üye";

        var memberBreakdown = memberRows
            .GroupBy(r => r.EffectiveUserId)
            .Select(g =>
            {
                var displayName = g.Select(x => x.DisplayName).FirstOrDefault(n => n != null) ?? unknownMemberLabel;
                var incomeAmount = g.Where(x => x.Type == TransactionType.Income).Sum(x => x.Amount);
                var expenseAmount = g.Where(x => x.Type == TransactionType.Expense).Sum(x => x.Amount);
                return new MemberBreakdownItemDto(
                    g.Key,
                    displayName,
                    incomeAmount,
                    expenseAmount,
                    g.Count());
            })
            .OrderByDescending(m => m.ExpenseAmount)
            .ThenByDescending(m => m.IncomeAmount)
            .ToList();

        return new AnalyticsSummaryDto(
            request.DateFrom,
            request.DateTo,
            currencyCode,
            totalIncome,
            totalExpense,
            netCashFlow,
            savingsRatePercent,
            categoryBreakdown,
            memberBreakdown);
    }
}
