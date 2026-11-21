using FinSense.Application.Common.Auditing;
using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Budgets.Contracts;
using FinSense.Application.Features.Categories.Contracts;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Budgets.Commands;

// ───────────────────────────── Upsert ─────────────────────────────

public sealed record UpsertBudgetCommand(
    Guid WorkspaceId,
    Guid? CategoryId,
    short Year,
    short Month,
    decimal LimitAmount,
    short AlertThresholdPercent = 80) : IRequest<BudgetDto>;

public sealed class UpsertBudgetCommandValidator : AbstractValidator<UpsertBudgetCommand>
{
    public UpsertBudgetCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.Year).InclusiveBetween((short)2000, (short)2100).WithMessage("Geçerli bir yıl belirtilmelidir.");
        RuleFor(x => x.Month).InclusiveBetween((short)1, (short)12).WithMessage("Ay 1 ile 12 arasında olmalıdır.");

        RuleFor(x => x.LimitAmount)
            .GreaterThan(0m).WithMessage("Bütçe limiti sıfırdan büyük olmalıdır.")
            .PrecisionScale(19, 4, ignoreTrailingZeros: true)
            .WithMessage("Bütçe limiti NUMERIC(19,4) hassasiyetini aşamaz.");

        RuleFor(x => x.AlertThresholdPercent)
            .InclusiveBetween((short)1, (short)100)
            .WithMessage("Uyarı eşiği yüzdesi 1 ile 100 arasında olmalıdır.");
    }
}

public sealed class UpsertBudgetCommandHandler : IRequestHandler<UpsertBudgetCommand, BudgetDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpsertBudgetCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<BudgetDto> Handle(UpsertBudgetCommand request, CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(request.WorkspaceId, WorkspaceRole.Admin, cancellationToken);
        var isEnglish = CategoryLocalization.IsEnglish(_currentUserService.Locale);
        var now = _dateTimeProvider.UtcNow;

        Category? category = null;
        if (request.CategoryId.HasValue)
        {
            category = await _dbContext.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    c => c.Id == request.CategoryId.Value && c.WorkspaceId == request.WorkspaceId && !c.IsDeleted,
                    cancellationToken)
                ?? throw new NotFoundException("CATEGORY_NOT_FOUND", "Kategori bu çalışma alanında bulunamadı.");

            if (category.Type != CategoryType.Expense)
            {
                throw new DomainException(
                    "BUDGET_CATEGORY_MUST_BE_EXPENSE",
                    "Bütçe yalnızca gider (Expense) kategorileri için oluşturulabilir.");
            }
        }

        var existingBudget = await _dbContext.Budgets
            .FirstOrDefaultAsync(
                b => b.WorkspaceId == request.WorkspaceId &&
                     b.Year == request.Year &&
                     b.Month == request.Month &&
                     b.CategoryId == request.CategoryId &&
                     !b.IsDeleted,
                cancellationToken);

        Budget budget;
        if (existingBudget is not null)
        {
            var previousLimit = existingBudget.LimitAmount;
            var previousThreshold = existingBudget.AlertThresholdPercent;

            existingBudget.UpdateLimit(request.LimitAmount, request.AlertThresholdPercent);
            budget = existingBudget;

            if (access.IsHousehold)
            {
                _dbContext.AuditLogs.Add(AuditLog.Create(
                    request.WorkspaceId,
                    access.UserId,
                    "BudgetUpdated",
                    nameof(Budget),
                    budget.Id,
                    oldValuesJson: AuditJson.Serialize(new { LimitAmount = previousLimit, AlertThresholdPercent = previousThreshold }),
                    newValuesJson: AuditJson.Serialize(new { budget.LimitAmount, budget.AlertThresholdPercent }),
                    utcNow: now));
            }
        }
        else
        {
            var currencyCode = await _dbContext.Workspaces
                .AsNoTracking()
                .Where(w => w.Id == request.WorkspaceId)
                .Select(w => w.DefaultCurrency)
                .FirstAsync(cancellationToken);

            budget = Budget.Create(
                request.WorkspaceId,
                request.Year,
                request.Month,
                request.LimitAmount,
                request.CategoryId,
                currencyCode,
                request.AlertThresholdPercent);

            _dbContext.Budgets.Add(budget);

            if (access.IsHousehold)
            {
                _dbContext.AuditLogs.Add(AuditLog.Create(
                    request.WorkspaceId,
                    access.UserId,
                    "BudgetCreated",
                    nameof(Budget),
                    budget.Id,
                    oldValuesJson: null,
                    newValuesJson: AuditJson.Serialize(new
                    {
                        budget.CategoryId,
                        budget.Year,
                        budget.Month,
                        budget.LimitAmount,
                        budget.AlertThresholdPercent
                    }),
                    utcNow: now));
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var periodStart = new DateOnly(budget.Year, budget.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        var expenseQuery = _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.WorkspaceId == request.WorkspaceId &&
                        !t.IsDeleted &&
                        t.Type == TransactionType.Expense &&
                        t.TransactionDate >= periodStart &&
                        t.TransactionDate <= periodEnd);

        if (budget.CategoryId.HasValue)
        {
            expenseQuery = expenseQuery.Where(t => t.CategoryId == budget.CategoryId.Value);
        }

        var spentAmount = await expenseQuery.SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;
        var remainingAmount = decimal.Round(budget.LimitAmount - spentAmount, 4, MidpointRounding.AwayFromZero);

        return new BudgetDto(
            budget.Id,
            budget.WorkspaceId,
            budget.CategoryId,
            category is not null ? (isEnglish && category.NameEn != null ? category.NameEn : category.NameTr) : null,
            category?.Icon,
            category?.ColorHex,
            budget.Year,
            budget.Month,
            budget.LimitAmount,
            spentAmount,
            remainingAmount,
            budget.CalculateUtilizationPercent(spentAmount),
            budget.AlertThresholdPercent,
            budget.EvaluateStatus(spentAmount),
            budget.CurrencyCode);
    }
}

// ───────────────────────────── Delete ─────────────────────────────

public sealed record DeleteBudgetCommand(Guid WorkspaceId, Guid BudgetId) : IRequest;

public sealed class DeleteBudgetCommandHandler : IRequestHandler<DeleteBudgetCommand>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly IDateTimeProvider _dateTimeProvider;

    public DeleteBudgetCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task Handle(DeleteBudgetCommand request, CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(request.WorkspaceId, WorkspaceRole.Admin, cancellationToken);

        var budget = await _dbContext.Budgets
            .FirstOrDefaultAsync(
                b => b.Id == request.BudgetId && b.WorkspaceId == request.WorkspaceId && !b.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException("BUDGET_NOT_FOUND", "Bütçe bu çalışma alanında bulunamadı.");

        budget.SoftDelete();

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId,
                access.UserId,
                "BudgetDeleted",
                nameof(Budget),
                budget.Id,
                oldValuesJson: AuditJson.Serialize(new { budget.CategoryId, budget.Year, budget.Month, budget.LimitAmount }),
                newValuesJson: null,
                utcNow: _dateTimeProvider.UtcNow));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
