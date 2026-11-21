using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Budgets.Contracts;
using FinSense.Application.Features.Categories.Contracts;
using FinSense.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Budgets.Queries.GetBudgets;

public sealed record GetBudgetsQuery(Guid WorkspaceId, short Year, short Month) : IRequest<IReadOnlyList<BudgetDto>>;

public sealed class GetBudgetsQueryValidator : AbstractValidator<GetBudgetsQuery>
{
    public GetBudgetsQueryValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.Year).InclusiveBetween((short)2000, (short)2100).WithMessage("Geçerli bir yıl belirtilmelidir.");
        RuleFor(x => x.Month).InclusiveBetween((short)1, (short)12).WithMessage("Ay 1 ile 12 arasında olmalıdır.");
    }
}

public sealed class GetBudgetsQueryHandler : IRequestHandler<GetBudgetsQuery, IReadOnlyList<BudgetDto>>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly ICurrentUserService _currentUserService;

    public GetBudgetsQueryHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _currentUserService = currentUserService;
    }

    public async Task<IReadOnlyList<BudgetDto>> Handle(GetBudgetsQuery request, CancellationToken cancellationToken)
    {
        await _workspaceAccess.EnsureAccessAsync(request.WorkspaceId, WorkspaceRole.Viewer, cancellationToken);
        var isEnglish = CategoryLocalization.IsEnglish(_currentUserService.Locale);

        var budgets = await _dbContext.Budgets
            .AsNoTracking()
            .Where(b => b.WorkspaceId == request.WorkspaceId &&
                        b.Year == request.Year &&
                        b.Month == request.Month &&
                        !b.IsDeleted)
            .OrderBy(b => b.CategoryId != null)
            .ThenBy(b => b.Category != null ? b.Category.NameTr : string.Empty)
            .Select(b => new
            {
                Budget = b,
                CategoryName = b.Category != null
                    ? (isEnglish && b.Category.NameEn != null ? b.Category.NameEn : b.Category.NameTr)
                    : null,
                CategoryIcon = b.Category != null ? b.Category.Icon : null,
                CategoryColorHex = b.Category != null ? b.Category.ColorHex : null
            })
            .ToListAsync(cancellationToken);

        if (budgets.Count == 0)
        {
            return [];
        }

        var periodStart = new DateOnly(request.Year, request.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        // Strictly sum only non-deleted Expense transactions (excludes Income and Transfer)
        var expenseTotalsByCategory = await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.WorkspaceId == request.WorkspaceId &&
                        !t.IsDeleted &&
                        t.Type == TransactionType.Expense &&
                        t.TransactionDate >= periodStart &&
                        t.TransactionDate <= periodEnd &&
                        t.CategoryId != null)
            .GroupBy(t => t.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Total = g.Sum(t => t.Amount) })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Total, cancellationToken);

        var totalMonthExpense = expenseTotalsByCategory.Values.Sum();

        return budgets.Select(row =>
        {
            var spentAmount = row.Budget.CategoryId.HasValue
                ? expenseTotalsByCategory.GetValueOrDefault(row.Budget.CategoryId.Value, 0m)
                : totalMonthExpense;

            var remainingAmount = decimal.Round(row.Budget.LimitAmount - spentAmount, 4, MidpointRounding.AwayFromZero);
            var utilizationPercent = row.Budget.CalculateUtilizationPercent(spentAmount);
            var status = row.Budget.EvaluateStatus(spentAmount);

            return new BudgetDto(
                row.Budget.Id,
                row.Budget.WorkspaceId,
                row.Budget.CategoryId,
                row.CategoryName,
                row.CategoryIcon,
                row.CategoryColorHex,
                row.Budget.Year,
                row.Budget.Month,
                row.Budget.LimitAmount,
                spentAmount,
                remainingAmount,
                utilizationPercent,
                row.Budget.AlertThresholdPercent,
                status,
                row.Budget.CurrencyCode);
        }).ToList();
    }
}
