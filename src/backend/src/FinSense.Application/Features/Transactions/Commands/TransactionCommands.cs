using FinSense.Application.Common.Auditing;
using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Categories.Contracts;
using FinSense.Application.Features.Transactions.Contracts;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Transactions.Commands;

internal static class TransactionCommandHelper
{
    public static async Task<Category?> ValidateCategoryAsync(
        IAppDbContext dbContext,
        Guid workspaceId,
        TransactionType type,
        Guid? categoryId,
        CancellationToken cancellationToken)
    {
        if (type == TransactionType.Transfer)
        {
            if (categoryId.HasValue)
            {
                throw new DomainException(
                    "TRANSFER_CATEGORY_MUST_BE_NULL",
                    "Transfer işlemlerinde kategori seçilemez.");
            }

            return null;
        }

        if (!categoryId.HasValue || categoryId.Value == Guid.Empty)
        {
            throw new DomainException(
                "TRANSACTION_CATEGORY_REQUIRED",
                "Gelir ve gider işlemlerinde kategori zorunludur.");
        }

        var category = await dbContext.Categories
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Id == categoryId.Value && c.WorkspaceId == workspaceId && !c.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException("CATEGORY_NOT_FOUND", "Kategori bu çalışma alanında bulunamadı.");

        var expectedCategoryType = type == TransactionType.Income
            ? CategoryType.Income
            : CategoryType.Expense;

        if (category.Type != expectedCategoryType)
        {
            throw new DomainException(
                "CATEGORY_TYPE_MISMATCH",
                "Seçilen kategori türü işlem türü (Gelir/Gider) ile uyuşmuyor.");
        }

        return category;
    }

    public static async Task EnsurePaidByMemberAsync(
        IAppDbContext dbContext,
        Guid workspaceId,
        Guid? paidByUserId,
        CancellationToken cancellationToken)
    {
        if (!paidByUserId.HasValue)
        {
            return;
        }

        var isMember = await dbContext.WorkspaceMembers
            .AsNoTracking()
            .AnyAsync(
                wm => wm.WorkspaceId == workspaceId && wm.UserId == paidByUserId.Value,
                cancellationToken);

        if (!isMember)
        {
            throw new NotFoundException(
                "WORKSPACE_MEMBER_NOT_FOUND",
                "Ödemeyi yapan kullanıcı bu çalışma alanının üyesi değil.");
        }
    }

    public static async Task<Dictionary<Guid, Wallet>> LoadWorkspaceWalletsAsync(
        IAppDbContext dbContext,
        Guid workspaceId,
        IEnumerable<Guid?> walletIds,
        CancellationToken cancellationToken)
    {
        var distinctIds = walletIds
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        if (distinctIds.Count == 0)
        {
            return [];
        }

        return await dbContext.Wallets
            .Where(w => w.WorkspaceId == workspaceId && !w.IsDeleted && distinctIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, cancellationToken);
    }

    public static (Wallet Primary, Wallet? Counterparty) ResolveRequiredWallets(
        IReadOnlyDictionary<Guid, Wallet> walletsById,
        Guid walletId,
        TransactionType type,
        Guid? counterpartyWalletId)
    {
        if (!walletsById.TryGetValue(walletId, out var primaryWallet))
        {
            throw new NotFoundException("WALLET_NOT_FOUND", "Cüzdan bu çalışma alanında bulunamadı.");
        }

        Wallet? counterpartyWallet = null;
        if (type == TransactionType.Transfer)
        {
            if (!counterpartyWalletId.HasValue || !walletsById.TryGetValue(counterpartyWalletId.Value, out counterpartyWallet))
            {
                throw new NotFoundException("COUNTERPARTY_WALLET_NOT_FOUND", "Hedef cüzdan bu çalışma alanında bulunamadı.");
            }
        }

        return (primaryWallet, counterpartyWallet);
    }

    public static void EnsureSufficientBalanceForTransfer(
        Wallet sourceWallet,
        TransactionType type,
        decimal amount,
        Transaction? existingTransactionBeingReplaced = null)
    {
        if (type != TransactionType.Transfer)
        {
            return;
        }

        var availableBalance = sourceWallet.CurrentBalance;

        if (existingTransactionBeingReplaced is not null)
        {
            if (existingTransactionBeingReplaced.Type == TransactionType.Income &&
                existingTransactionBeingReplaced.WalletId == sourceWallet.Id)
            {
                availableBalance -= existingTransactionBeingReplaced.Amount;
            }
            else if (existingTransactionBeingReplaced.Type == TransactionType.Expense &&
                     existingTransactionBeingReplaced.WalletId == sourceWallet.Id)
            {
                availableBalance += existingTransactionBeingReplaced.Amount;
            }
            else if (existingTransactionBeingReplaced.Type == TransactionType.Transfer)
            {
                if (existingTransactionBeingReplaced.WalletId == sourceWallet.Id)
                {
                    availableBalance += existingTransactionBeingReplaced.Amount;
                }

                if (existingTransactionBeingReplaced.CounterpartyWalletId == sourceWallet.Id)
                {
                    availableBalance -= existingTransactionBeingReplaced.Amount;
                }
            }
        }

        var roundedAmount = decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
        if (availableBalance <= 0m || roundedAmount > availableBalance)
        {
            throw new DomainException(
                "INSUFFICIENT_WALLET_BALANCE_FOR_TRANSFER",
                "Transfer tutarı kaynak cüzdanın mevcut bakiyesini aşamaz.");
        }
    }

    public static async Task<TransactionDetailDto> LoadDetailAsync(
        IAppDbContext dbContext,
        Guid workspaceId,
        Guid transactionId,
        string locale,
        CancellationToken cancellationToken)
    {
        var isEnglish = CategoryLocalization.IsEnglish(locale);

        return await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.Id == transactionId && t.WorkspaceId == workspaceId && !t.IsDeleted)
            .Select(t => new TransactionDetailDto(
                t.Id,
                t.WorkspaceId,
                t.WalletId,
                t.Wallet.Name,
                t.CounterpartyWalletId,
                t.CounterpartyWallet != null ? t.CounterpartyWallet.Name : null,
                t.CategoryId,
                t.Category != null
                    ? (isEnglish && t.Category.NameEn != null ? t.Category.NameEn : t.Category.NameTr)
                    : null,
                t.Category != null ? t.Category.Icon : null,
                t.Category != null ? t.Category.ColorHex : null,
                t.Type,
                t.Amount,
                t.CurrencyCode,
                t.OriginalAmount,
                t.OriginalCurrencyCode,
                t.ExchangeRate,
                t.TransactionDate,
                t.MerchantName,
                t.Notes,
                t.CreatedByUserId,
                t.CreatedByUser != null ? t.CreatedByUser.DisplayName : null,
                t.PaidByUserId,
                t.PaidByUser != null ? t.PaidByUser.DisplayName : null,
                t.IdempotencyKey,
                t.CreatedAtUtc,
                t.Receipt != null
                    ? new TransactionReceiptDto(
                        t.Receipt.Id,
                        t.Receipt.ContentType,
                        t.Receipt.FileSizeBytes,
                        t.Receipt.Status,
                        t.Receipt.ExtractedMerchant,
                        t.Receipt.ExtractedDate,
                        t.Receipt.ExtractedTotal,
                        t.Receipt.ExtractedTax,
                        t.Receipt.ConfidenceScore,
                        t.Receipt.CreatedAtUtc)
                    : null))
            .FirstAsync(cancellationToken);
    }
}

// ───────────────────────────── Create ─────────────────────────────

public sealed record CreateTransactionCommand(
    Guid WorkspaceId,
    TransactionType Type,
    Guid WalletId,
    Guid? CounterpartyWalletId,
    Guid? CategoryId,
    Guid? ReceiptId,
    decimal Amount,
    string CurrencyCode,
    DateOnly TransactionDate,
    string? MerchantName,
    string? Notes,
    Guid? PaidByUserId,
    string? IdempotencyKey = null,
    int? InstallmentCount = null) : IRequest<TransactionDetailDto>;

public sealed class CreateTransactionCommandValidator : AbstractValidator<CreateTransactionCommand>
{
    public CreateTransactionCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.WalletId).NotEmpty().WithMessage("Cüzdan kimliği zorunludur.");
        RuleFor(x => x.Type).IsInEnum().WithMessage("Geçersiz işlem türü.");

        RuleFor(x => x.Amount)
            .GreaterThan(0m).WithMessage("İşlem tutarı sıfırdan büyük olmalıdır.")
            .PrecisionScale(19, 4, ignoreTrailingZeros: true)
            .WithMessage("İşlem tutarı NUMERIC(19,4) hassasiyetini aşamaz.");

        RuleFor(x => x.CurrencyCode)
            .NotEmpty()
            .Length(3).WithMessage("Para birimi kodu 3 karakterli ISO 4217 formatında olmalıdır.");

        RuleFor(x => x.MerchantName).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.IdempotencyKey).MaximumLength(100);

        RuleFor(x => x.InstallmentCount)
            .InclusiveBetween(1, 24)
            .When(x => x.InstallmentCount.HasValue)
            .WithMessage("Taksit sayısı 1 ile 24 arasında olmalıdır.");

        RuleFor(x => x.InstallmentCount)
            .Must(c => !c.HasValue || c.Value == 1)
            .When(x => x.Type != TransactionType.Expense)
            .WithMessage("Taksitlendirme yalnızca gider işlemlerinde kullanılabilir.");

        When(x => x.Type == TransactionType.Transfer, () =>
        {
            RuleFor(x => x.CounterpartyWalletId)
                .NotEmpty().WithMessage("Transfer işlemlerinde hedef cüzdan zorunludur.")
                .NotEqual(x => x.WalletId).WithMessage("Kaynak cüzdan ile hedef cüzdan aynı olamaz.");

            RuleFor(x => x.CategoryId)
                .Null().WithMessage("Transfer işlemlerinde kategori seçilemez.");
        });

        When(x => x.Type != TransactionType.Transfer, () =>
        {
            RuleFor(x => x.CategoryId)
                .NotEmpty().WithMessage("Gelir ve gider işlemlerinde kategori zorunludur.");

            RuleFor(x => x.CounterpartyWalletId)
                .Null().WithMessage("Yalnızca transfer işlemlerinde hedef cüzdan belirtilebilir.");
        });
    }
}

public sealed class CreateTransactionCommandHandler
    : IRequestHandler<CreateTransactionCommand, TransactionDetailDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateTransactionCommandHandler(
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

    public async Task<TransactionDetailDto> Handle(
        CreateTransactionCommand request,
        CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Contributor,
            cancellationToken);

        var normalizedIdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? null
            : request.IdempotencyKey.Trim();

        if (normalizedIdempotencyKey is not null)
        {
            var existing = await _dbContext.Transactions
                .AsNoTracking()
                .Where(t => t.WorkspaceId == request.WorkspaceId && t.IdempotencyKey == normalizedIdempotencyKey)
                .Select(t => new { t.Id, t.IsDeleted })
                .FirstOrDefaultAsync(cancellationToken);

            if (existing is not null)
            {
                if (existing.IsDeleted)
                {
                    throw new ConflictException(
                        "IDEMPOTENCY_KEY_ALREADY_USED",
                        "Bu Idempotency-Key daha önce silinmiş bir işlem için kullanılmıştır.");
                }

                return await TransactionCommandHelper.LoadDetailAsync(
                    _dbContext,
                    request.WorkspaceId,
                    existing.Id,
                    _currentUserService.Locale,
                    cancellationToken);
            }
        }

        await TransactionCommandHelper.ValidateCategoryAsync(
            _dbContext, request.WorkspaceId, request.Type, request.CategoryId, cancellationToken);

        await TransactionCommandHelper.EnsurePaidByMemberAsync(
            _dbContext, request.WorkspaceId, request.PaidByUserId, cancellationToken);

        var walletsById = await TransactionCommandHelper.LoadWorkspaceWalletsAsync(
            _dbContext,
            request.WorkspaceId,
            [request.WalletId, request.CounterpartyWalletId],
            cancellationToken);

        var (primaryWallet, counterpartyWallet) = TransactionCommandHelper.ResolveRequiredWallets(
            walletsById,
            request.WalletId,
            request.Type,
            request.CounterpartyWalletId);

        if (!string.Equals(primaryWallet.CurrencyCode, request.CurrencyCode.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(
                "CURRENCY_MISMATCH",
                "İşlem para birimi seçilen cüzdanın para birimi ile uyuşmalıdır.");
        }

        if (counterpartyWallet is not null &&
            !string.Equals(primaryWallet.CurrencyCode, counterpartyWallet.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(
                "TRANSFER_CURRENCY_MISMATCH",
                "Transfer işlemi yalnızca aynı para birimine sahip cüzdanlar arasında yapılabilir.");
        }

        TransactionCommandHelper.EnsureSufficientBalanceForTransfer(
            primaryWallet,
            request.Type,
            request.Amount);

        Receipt? receipt = null;
        if (request.ReceiptId.HasValue)
        {
            receipt = await _dbContext.Receipts
                .FirstOrDefaultAsync(
                    r => r.Id == request.ReceiptId.Value && r.WorkspaceId == request.WorkspaceId,
                    cancellationToken)
                ?? throw new NotFoundException("RECEIPT_NOT_FOUND", "Fiş bu çalışma alanında bulunamadı.");

            var alreadyLinked = await _dbContext.Transactions
                .AsNoTracking()
                .AnyAsync(t => t.ReceiptId == receipt.Id, cancellationToken);

            if (alreadyLinked || receipt.Status == ReceiptStatus.LinkedToTransaction)
            {
                throw new ConflictException(
                    "RECEIPT_ALREADY_LINKED",
                    "Bu fiş zaten başka bir işleme bağlanmış.");
            }

            receipt.LinkToTransaction();
        }

        var now = _dateTimeProvider.UtcNow;
        var installmentCount = request.InstallmentCount ?? 1;
        if (installmentCount < 1 || installmentCount > 24)
        {
            throw new DomainException(
                "INVALID_INSTALLMENT_COUNT",
                "Taksit sayısı 1 ile 24 arasında olmalıdır.");
        }

        if (installmentCount > 1 && request.Type != TransactionType.Expense)
        {
            throw new DomainException(
                "INSTALLMENT_EXPENSE_ONLY",
                "Taksitlendirme yalnızca gider işlemlerinde kullanılabilir.");
        }

        Transaction firstTransaction;

        if (request.Type == TransactionType.Expense && installmentCount > 1)
        {
            // Split total into exact kuruş installments, placing any rounding remainder on the first installment
            // so the sum across all installments equals the exact requested total.
            var totalKurus = (long)decimal.Round(request.Amount * 100m, 0, MidpointRounding.AwayFromZero);
            var baseKurus = totalKurus / installmentCount;
            var remainderKurus = totalKurus % installmentCount;

            if (baseKurus <= 0)
            {
                throw new DomainException(
                    "INSTALLMENT_AMOUNT_TOO_SMALL",
                    "Toplam tutar seçilen taksit sayısına bölünmek için çok küçük.");
            }

            var createdTransactions = new List<Transaction>(installmentCount);
            var trimmedMerchant = string.IsNullOrWhiteSpace(request.MerchantName) ? null : request.MerchantName.Trim();
            var trimmedNotes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

            for (var i = 0; i < installmentCount; i++)
            {
                var installmentAmount = (i == 0 ? baseKurus + remainderKurus : baseKurus) / 100m;
                var installmentDate = request.TransactionDate.AddMonths(i);
                var installmentTag = $"(Taksit {i + 1}/{installmentCount})";

                var installmentMerchant = trimmedMerchant is null
                    ? null
                    : AppendTagWithinLimit(trimmedMerchant, installmentTag, 200);

                var installmentNotes = trimmedNotes is null
                    ? installmentTag
                    : AppendTagWithinLimit(trimmedNotes, installmentTag, 1000);

                var installmentIdempotencyKey = normalizedIdempotencyKey is null
                    ? null
                    : (i == 0
                        ? normalizedIdempotencyKey
                        : TruncateIdempotencyKey($"{normalizedIdempotencyKey}-inst-{i + 1}"));

                var installmentTx = Transaction.Create(
                    workspaceId: request.WorkspaceId,
                    walletId: request.WalletId,
                    type: TransactionType.Expense,
                    amount: installmentAmount,
                    transactionDate: installmentDate,
                    categoryId: request.CategoryId,
                    counterpartyWalletId: null,
                    receiptId: i == 0 ? request.ReceiptId : null,
                    currencyCode: primaryWallet.CurrencyCode,
                    merchantName: installmentMerchant,
                    notes: installmentNotes,
                    createdByUserId: access.UserId,
                    paidByUserId: request.PaidByUserId ?? access.UserId,
                    idempotencyKey: installmentIdempotencyKey,
                    utcNow: now);

                // Apply each installment to the wallet consistently with existing Transaction balance logic:
                // full liability is reflected on the wallet/credit card immediately, while deleting any
                // individual installment cleanly reverts its exact amount. Monthly analytics & budgets
                // filter by TransactionDate, so each month reflects only that month's installment.
                installmentTx.ApplyToWallets(primaryWallet, null);

                _dbContext.Transactions.Add(installmentTx);
                createdTransactions.Add(installmentTx);

                if (access.IsHousehold)
                {
                    _dbContext.AuditLogs.Add(AuditLog.Create(
                        request.WorkspaceId,
                        access.UserId,
                        "TransactionCreated",
                        nameof(Transaction),
                        installmentTx.Id,
                        oldValuesJson: null,
                        newValuesJson: AuditJson.Serialize(new
                        {
                            installmentTx.Type,
                            installmentTx.WalletId,
                            installmentTx.CategoryId,
                            installmentTx.Amount,
                            installmentTx.TransactionDate,
                            InstallmentIndex = i + 1,
                            InstallmentCount = installmentCount
                        }),
                        utcNow: now));
                }
            }

            firstTransaction = createdTransactions[0];
        }
        else
        {
            firstTransaction = Transaction.Create(
                workspaceId: request.WorkspaceId,
                walletId: request.WalletId,
                type: request.Type,
                amount: request.Amount,
                transactionDate: request.TransactionDate,
                categoryId: request.CategoryId,
                counterpartyWalletId: request.CounterpartyWalletId,
                receiptId: request.ReceiptId,
                currencyCode: primaryWallet.CurrencyCode,
                merchantName: request.MerchantName,
                notes: request.Notes,
                createdByUserId: access.UserId,
                paidByUserId: request.PaidByUserId ?? access.UserId,
                idempotencyKey: normalizedIdempotencyKey,
                utcNow: now);

            firstTransaction.ApplyToWallets(primaryWallet, counterpartyWallet);

            _dbContext.Transactions.Add(firstTransaction);

            if (access.IsHousehold)
            {
                _dbContext.AuditLogs.Add(AuditLog.Create(
                    request.WorkspaceId,
                    access.UserId,
                    "TransactionCreated",
                    nameof(Transaction),
                    firstTransaction.Id,
                    oldValuesJson: null,
                    newValuesJson: AuditJson.Serialize(new
                    {
                        firstTransaction.Type,
                        firstTransaction.WalletId,
                        firstTransaction.CounterpartyWalletId,
                        firstTransaction.CategoryId,
                        firstTransaction.Amount,
                        firstTransaction.TransactionDate
                    }),
                    utcNow: now));
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await TransactionCommandHelper.LoadDetailAsync(
            _dbContext,
            request.WorkspaceId,
            firstTransaction.Id,
            _currentUserService.Locale,
            cancellationToken);
    }

    private static string AppendTagWithinLimit(string baseText, string tag, int maxLength)
    {
        var suffix = $" {tag}";
        if (baseText.Length + suffix.Length <= maxLength)
        {
            return $"{baseText}{suffix}";
        }

        var allowedBase = Math.Max(0, maxLength - suffix.Length);
        return $"{baseText[..allowedBase].TrimEnd()}{suffix}";
    }

    private static string TruncateIdempotencyKey(string key) =>
        key.Length <= 100 ? key : key[..100];
}

// ───────────────────────────── Update ─────────────────────────────

public sealed record UpdateTransactionCommand(
    Guid WorkspaceId,
    Guid TransactionId,
    TransactionType Type,
    Guid WalletId,
    Guid? CounterpartyWalletId,
    Guid? CategoryId,
    decimal Amount,
    DateOnly TransactionDate,
    string? MerchantName,
    string? Notes,
    Guid? PaidByUserId) : IRequest<TransactionDetailDto>;

public sealed class UpdateTransactionCommandValidator : AbstractValidator<UpdateTransactionCommand>
{
    public UpdateTransactionCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.TransactionId).NotEmpty();
        RuleFor(x => x.WalletId).NotEmpty().WithMessage("Cüzdan kimliği zorunludur.");
        RuleFor(x => x.Type).IsInEnum().WithMessage("Geçersiz işlem türü.");

        RuleFor(x => x.Amount)
            .GreaterThan(0m).WithMessage("İşlem tutarı sıfırdan büyük olmalıdır.")
            .PrecisionScale(19, 4, ignoreTrailingZeros: true)
            .WithMessage("İşlem tutarı NUMERIC(19,4) hassasiyetini aşamaz.");

        RuleFor(x => x.MerchantName).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(1000);

        When(x => x.Type == TransactionType.Transfer, () =>
        {
            RuleFor(x => x.CounterpartyWalletId)
                .NotEmpty().WithMessage("Transfer işlemlerinde hedef cüzdan zorunludur.")
                .NotEqual(x => x.WalletId).WithMessage("Kaynak cüzdan ile hedef cüzdan aynı olamaz.");

            RuleFor(x => x.CategoryId)
                .Null().WithMessage("Transfer işlemlerinde kategori seçilemez.");
        });

        When(x => x.Type != TransactionType.Transfer, () =>
        {
            RuleFor(x => x.CategoryId)
                .NotEmpty().WithMessage("Gelir ve gider işlemlerinde kategori zorunludur.");

            RuleFor(x => x.CounterpartyWalletId)
                .Null().WithMessage("Yalnızca transfer işlemlerinde hedef cüzdan belirtilebilir.");
        });
    }
}

public sealed class UpdateTransactionCommandHandler
    : IRequestHandler<UpdateTransactionCommand, TransactionDetailDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateTransactionCommandHandler(
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

    public async Task<TransactionDetailDto> Handle(
        UpdateTransactionCommand request,
        CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Contributor,
            cancellationToken);

        var transaction = await _dbContext.Transactions
            .FirstOrDefaultAsync(
                t => t.Id == request.TransactionId && t.WorkspaceId == request.WorkspaceId && !t.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException("TRANSACTION_NOT_FOUND", "İşlem bu çalışma alanında bulunamadı.");

        if (!access.Role.CanMutateOtherMembersTransactions() && transaction.CreatedByUserId != access.UserId)
        {
            throw new ForbiddenAccessException(
                "TRANSACTION_EDIT_FORBIDDEN",
                "Katkıda Bulunan (Contributor) rolündeki üyeler yalnızca kendi oluşturdukları işlemleri düzenleyebilir.");
        }

        await TransactionCommandHelper.ValidateCategoryAsync(
            _dbContext, request.WorkspaceId, request.Type, request.CategoryId, cancellationToken);

        await TransactionCommandHelper.EnsurePaidByMemberAsync(
            _dbContext, request.WorkspaceId, request.PaidByUserId, cancellationToken);

        var walletsById = await TransactionCommandHelper.LoadWorkspaceWalletsAsync(
            _dbContext,
            request.WorkspaceId,
            [transaction.WalletId, transaction.CounterpartyWalletId, request.WalletId, request.CounterpartyWalletId],
            cancellationToken);

        var (oldPrimaryWallet, oldCounterpartyWallet) = TransactionCommandHelper.ResolveRequiredWallets(
            walletsById,
            transaction.WalletId,
            transaction.Type,
            transaction.CounterpartyWalletId);

        var (newPrimaryWallet, newCounterpartyWallet) = TransactionCommandHelper.ResolveRequiredWallets(
            walletsById,
            request.WalletId,
            request.Type,
            request.CounterpartyWalletId);

        if (newCounterpartyWallet is not null &&
            !string.Equals(newPrimaryWallet.CurrencyCode, newCounterpartyWallet.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(
                "TRANSFER_CURRENCY_MISMATCH",
                "Transfer işlemi yalnızca aynı para birimine sahip cüzdanlar arasında yapılabilir.");
        }

        var before = new
        {
            transaction.Type,
            transaction.WalletId,
            transaction.CounterpartyWalletId,
            transaction.CategoryId,
            transaction.Amount,
            transaction.TransactionDate,
            transaction.MerchantName,
            transaction.Notes,
            transaction.PaidByUserId
        };

        // Validate transfer balance against post-reversal source wallet balance before mutating any tracked entities
        TransactionCommandHelper.EnsureSufficientBalanceForTransfer(
            newPrimaryWallet,
            request.Type,
            request.Amount,
            transaction);

        // Atomically reverse old balance impacts, mutate entity, and apply new balance impacts
        transaction.RevertFromWallets(oldPrimaryWallet, oldCounterpartyWallet);

        transaction.Update(
            request.WalletId,
            request.Type,
            request.Amount,
            request.TransactionDate,
            request.CategoryId,
            request.CounterpartyWalletId,
            request.MerchantName,
            request.Notes,
            request.PaidByUserId);

        transaction.ApplyToWallets(newPrimaryWallet, newCounterpartyWallet);

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId,
                access.UserId,
                "TransactionUpdated",
                nameof(Transaction),
                transaction.Id,
                oldValuesJson: AuditJson.Serialize(before),
                newValuesJson: AuditJson.Serialize(new
                {
                    transaction.Type,
                    transaction.WalletId,
                    transaction.CounterpartyWalletId,
                    transaction.CategoryId,
                    transaction.Amount,
                    transaction.TransactionDate,
                    transaction.MerchantName,
                    transaction.Notes,
                    transaction.PaidByUserId
                }),
                utcNow: _dateTimeProvider.UtcNow));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await TransactionCommandHelper.LoadDetailAsync(
            _dbContext,
            request.WorkspaceId,
            transaction.Id,
            _currentUserService.Locale,
            cancellationToken);
    }
}

// ───────────────────────────── Delete ─────────────────────────────

public sealed record DeleteTransactionCommand(Guid WorkspaceId, Guid TransactionId) : IRequest;

public sealed class DeleteTransactionCommandHandler : IRequestHandler<DeleteTransactionCommand>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly IDateTimeProvider _dateTimeProvider;

    public DeleteTransactionCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task Handle(DeleteTransactionCommand request, CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Contributor,
            cancellationToken);

        var transaction = await _dbContext.Transactions
            .FirstOrDefaultAsync(
                t => t.Id == request.TransactionId && t.WorkspaceId == request.WorkspaceId && !t.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException("TRANSACTION_NOT_FOUND", "İşlem bu çalışma alanında bulunamadı.");

        if (!access.Role.CanMutateOtherMembersTransactions() && transaction.CreatedByUserId != access.UserId)
        {
            throw new ForbiddenAccessException(
                "TRANSACTION_DELETE_FORBIDDEN",
                "Katkıda Bulunan (Contributor) rolündeki üyeler yalnızca kendi oluşturdukları işlemleri silebilir.");
        }

        var walletsById = await TransactionCommandHelper.LoadWorkspaceWalletsAsync(
            _dbContext,
            request.WorkspaceId,
            [transaction.WalletId, transaction.CounterpartyWalletId],
            cancellationToken);

        var (primaryWallet, counterpartyWallet) = TransactionCommandHelper.ResolveRequiredWallets(
            walletsById,
            transaction.WalletId,
            transaction.Type,
            transaction.CounterpartyWalletId);

        var now = _dateTimeProvider.UtcNow;

        transaction.SoftDelete(now, primaryWallet, counterpartyWallet);

        if (transaction.ReceiptId.HasValue)
        {
            var receipt = await _dbContext.Receipts
                .FirstOrDefaultAsync(
                    r => r.Id == transaction.ReceiptId.Value && r.WorkspaceId == request.WorkspaceId,
                    cancellationToken);

            receipt?.MarkForDeletion();
        }

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId,
                access.UserId,
                "TransactionDeleted",
                nameof(Transaction),
                transaction.Id,
                oldValuesJson: AuditJson.Serialize(new
                {
                    transaction.Type,
                    transaction.WalletId,
                    transaction.CounterpartyWalletId,
                    transaction.CategoryId,
                    transaction.Amount,
                    transaction.TransactionDate
                }),
                newValuesJson: null,
                utcNow: now));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
