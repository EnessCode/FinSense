using FinSense.Application.Common.Auditing;
using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Categories.Contracts;
using FinSense.Application.Features.RecurringTransactions.Contracts;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.RecurringTransactions.Commands;

// ───────────────────────────── Create ─────────────────────────────

public sealed record CreateRecurringTransactionCommand(
    Guid WorkspaceId,
    Guid WalletId,
    Guid CategoryId,
    TransactionType Type,
    decimal Amount,
    string Title,
    int DayOfMonth,
    int? TotalOccurrences = null,
    bool ExecuteFirstImmediately = false) : IRequest<RecurringTransactionDto>;

public sealed class CreateRecurringTransactionCommandValidator
    : AbstractValidator<CreateRecurringTransactionCommand>
{
    public CreateRecurringTransactionCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.WalletId).NotEmpty().WithMessage("Cüzdan kimliği zorunludur.");
        RuleFor(x => x.CategoryId).NotEmpty().WithMessage("Kategori kimliği zorunludur.");

        RuleFor(x => x.Type)
            .Must(t => t is TransactionType.Income or TransactionType.Expense)
            .WithMessage("Düzenli işlemler yalnızca Gelir (Income) veya Gider (Expense) türünde olabilir.");

        RuleFor(x => x.Amount)
            .GreaterThan(0m).WithMessage("Tutar sıfırdan büyük olmalıdır.")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("Tutar en fazla 2 ondalık basamak içerebilir.");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Başlık zorunludur.")
            .MaximumLength(150).WithMessage("Başlık en fazla 150 karakter olabilir.");

        RuleFor(x => x.DayOfMonth)
            .InclusiveBetween(1, 31)
            .WithMessage("Ayın günü 1 ile 31 arasında olmalıdır.");

        RuleFor(x => x.TotalOccurrences!.Value)
            .InclusiveBetween(1, 120)
            .When(x => x.TotalOccurrences.HasValue)
            .WithMessage("Toplam dönem sayısı 1 ile 120 arasında olmalıdır.");
    }
}

public sealed class CreateRecurringTransactionCommandHandler
    : IRequestHandler<CreateRecurringTransactionCommand, RecurringTransactionDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateRecurringTransactionCommandHandler(
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

    public async Task<RecurringTransactionDto> Handle(
        CreateRecurringTransactionCommand request,
        CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Contributor,
            cancellationToken);

        var wallet = await _dbContext.Wallets
            .FirstOrDefaultAsync(
                w => w.Id == request.WalletId && w.WorkspaceId == request.WorkspaceId && !w.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException("WALLET_NOT_FOUND", "Cüzdan bu çalışma alanında bulunamadı.");

        if (wallet.IsArchived)
        {
            throw new DomainException("WALLET_ARCHIVED", "Arşivlenmiş cüzdana düzenli işlem tanımlanamaz.");
        }

        var category = await _dbContext.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Id == request.CategoryId && c.WorkspaceId == request.WorkspaceId && !c.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException("CATEGORY_NOT_FOUND", "Kategori bu çalışma alanında bulunamadı.");

        var expectedCategoryType = request.Type == TransactionType.Income
            ? CategoryType.Income
            : CategoryType.Expense;

        if (category.Type != expectedCategoryType)
        {
            throw new DomainException(
                "CATEGORY_TYPE_MISMATCH",
                "Seçilen kategori türü düzenli işlem türü (Gelir/Gider) ile uyuşmuyor.");
        }

        var now = _dateTimeProvider.UtcNow;

        var rule = RecurringTransaction.Create(
            workspaceId: request.WorkspaceId,
            walletId: wallet.Id,
            categoryId: category.Id,
            createdByUserId: access.UserId,
            type: request.Type,
            amount: request.Amount,
            title: request.Title,
            dayOfMonth: request.DayOfMonth,
            totalOccurrences: request.TotalOccurrences,
            currency: wallet.CurrencyCode,
            executeFirstImmediately: request.ExecuteFirstImmediately,
            utcNow: now);

        if (request.ExecuteFirstImmediately || now.Day == request.DayOfMonth)
        {
            var idempotencyKey = $"recurring-{rule.Id}-{now:yyyyMMdd}";
            var firstTransaction = Transaction.Create(
                workspaceId: rule.WorkspaceId,
                walletId: rule.WalletId,
                type: rule.Type,
                amount: rule.Amount,
                transactionDate: DateOnly.FromDateTime(now),
                categoryId: rule.CategoryId,
                counterpartyWalletId: null,
                receiptId: null,
                currencyCode: rule.Currency,
                merchantName: rule.Title,
                notes: rule.FormatCycleDescription(1),
                createdByUserId: access.UserId,
                paidByUserId: access.UserId,
                idempotencyKey: idempotencyKey,
                utcNow: now);

            firstTransaction.ApplyToWallets(wallet);
            rule.RecordInitialImmediateExecution(now);

            _dbContext.Transactions.Add(firstTransaction);
        }

        _dbContext.RecurringTransactions.Add(rule);

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId,
                access.UserId,
                "RecurringTransactionCreated",
                nameof(RecurringTransaction),
                rule.Id,
                oldValuesJson: null,
                newValuesJson: AuditJson.Serialize(new
                {
                    rule.Title,
                    rule.Type,
                    rule.Amount,
                    rule.DayOfMonth,
                    rule.TotalOccurrences,
                    rule.CompletedOccurrences
                }),
                utcNow: now));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var isEnglish = CategoryLocalization.IsEnglish(_currentUserService.Locale);
        var categoryName = isEnglish && category.NameEn != null ? category.NameEn : category.NameTr;

        return new RecurringTransactionDto(
            rule.Id,
            rule.WalletId,
            wallet.Name,
            rule.CategoryId,
            categoryName,
            rule.Type,
            rule.Amount,
            rule.Currency,
            rule.Title,
            rule.DayOfMonth,
            rule.TotalOccurrences,
            rule.CompletedOccurrences,
            rule.TotalOccurrences.HasValue
                ? Math.Max(0, rule.TotalOccurrences.Value - rule.CompletedOccurrences)
                : null,
            rule.NextExecutionDateUtc,
            rule.LastExecutedAtUtc,
            rule.IsActive);
    }
}

// ───────────────────────────── Toggle ─────────────────────────────

public sealed record ToggleRecurringTransactionCommand(Guid WorkspaceId, Guid RecurringTransactionId)
    : IRequest<RecurringTransactionDto>;

public sealed class ToggleRecurringTransactionCommandHandler
    : IRequestHandler<ToggleRecurringTransactionCommand, RecurringTransactionDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ToggleRecurringTransactionCommandHandler(
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

    public async Task<RecurringTransactionDto> Handle(
        ToggleRecurringTransactionCommand request,
        CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Contributor,
            cancellationToken);

        var rule = await _dbContext.RecurringTransactions
            .Include(r => r.Wallet)
            .Include(r => r.Category)
            .FirstOrDefaultAsync(
                r => r.Id == request.RecurringTransactionId && r.WorkspaceId == request.WorkspaceId,
                cancellationToken)
            ?? throw new NotFoundException("RECURRING_TRANSACTION_NOT_FOUND", "Düzenli işlem bu çalışma alanında bulunamadı.");

        if (!access.Role.CanMutateOtherMembersTransactions() && rule.CreatedByUserId != access.UserId)
        {
            throw new ForbiddenAccessException(
                "RECURRING_TRANSACTION_FORBIDDEN",
                "Katkıda Bulunan (Contributor) rolündeki üyeler yalnızca kendi oluşturdukları düzenli işlemleri yönetebilir.");
        }

        var now = _dateTimeProvider.UtcNow;
        var previousState = rule.IsActive;
        rule.Toggle(now);

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId,
                access.UserId,
                "RecurringTransactionToggled",
                nameof(RecurringTransaction),
                rule.Id,
                oldValuesJson: AuditJson.Serialize(new { IsActive = previousState }),
                newValuesJson: AuditJson.Serialize(new { rule.IsActive, rule.NextExecutionDateUtc }),
                utcNow: now));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var isEnglish = CategoryLocalization.IsEnglish(_currentUserService.Locale);
        var categoryName = isEnglish && rule.Category.NameEn != null ? rule.Category.NameEn : rule.Category.NameTr;

        return new RecurringTransactionDto(
            rule.Id,
            rule.WalletId,
            rule.Wallet.Name,
            rule.CategoryId,
            categoryName,
            rule.Type,
            rule.Amount,
            rule.Currency,
            rule.Title,
            rule.DayOfMonth,
            rule.TotalOccurrences,
            rule.CompletedOccurrences,
            rule.TotalOccurrences.HasValue
                ? Math.Max(0, rule.TotalOccurrences.Value - rule.CompletedOccurrences)
                : null,
            rule.NextExecutionDateUtc,
            rule.LastExecutedAtUtc,
            rule.IsActive);
    }
}

// ───────────────────────────── Delete ─────────────────────────────

public sealed record DeleteRecurringTransactionCommand(Guid WorkspaceId, Guid RecurringTransactionId)
    : IRequest;

public sealed class DeleteRecurringTransactionCommandHandler : IRequestHandler<DeleteRecurringTransactionCommand>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly IDateTimeProvider _dateTimeProvider;

    public DeleteRecurringTransactionCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task Handle(DeleteRecurringTransactionCommand request, CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Contributor,
            cancellationToken);

        var rule = await _dbContext.RecurringTransactions
            .FirstOrDefaultAsync(
                r => r.Id == request.RecurringTransactionId && r.WorkspaceId == request.WorkspaceId,
                cancellationToken)
            ?? throw new NotFoundException("RECURRING_TRANSACTION_NOT_FOUND", "Düzenli işlem bu çalışma alanında bulunamadı.");

        if (!access.Role.CanMutateOtherMembersTransactions() && rule.CreatedByUserId != access.UserId)
        {
            throw new ForbiddenAccessException(
                "RECURRING_TRANSACTION_FORBIDDEN",
                "Katkıda Bulunan (Contributor) rolündeki üyeler yalnızca kendi oluşturdukları düzenli işlemleri silebilir.");
        }

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId,
                access.UserId,
                "RecurringTransactionDeleted",
                nameof(RecurringTransaction),
                rule.Id,
                oldValuesJson: AuditJson.Serialize(new
                {
                    rule.Title,
                    rule.Type,
                    rule.Amount,
                    rule.CompletedOccurrences,
                    rule.TotalOccurrences
                }),
                newValuesJson: null,
                utcNow: _dateTimeProvider.UtcNow));
        }

        // Remove only the recurring schedule; historical ledger transactions remain intact
        _dbContext.RecurringTransactions.Remove(rule);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
