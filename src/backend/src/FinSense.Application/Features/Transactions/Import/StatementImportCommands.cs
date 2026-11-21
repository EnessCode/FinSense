using FinSense.Application.Common.Auditing;
using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Categories.Contracts;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinSense.Application.Features.Transactions.Import;

// ───────────────────────────── Preview ─────────────────────────────

public sealed record PreviewStatementImportCommand(
    Guid WorkspaceId,
    Guid WalletId,
    string FileName,
    byte[] FileContent,
    string? ContentType = null) : IRequest<ImportPreviewResultDto>;

public sealed class PreviewStatementImportCommandHandler
    : IRequestHandler<PreviewStatementImportCommand, ImportPreviewResultDto>
{
    public const int MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<PreviewStatementImportCommandHandler>? _logger;

    public PreviewStatementImportCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        ICurrentUserService currentUserService,
        ILogger<PreviewStatementImportCommandHandler>? logger = null)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ImportPreviewResultDto> Handle(
        PreviewStatementImportCommand request,
        CancellationToken cancellationToken)
    {
        await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Contributor,
            cancellationToken);

        if (request.FileContent is null || request.FileContent.Length == 0)
        {
            throw new DomainException("EMPTY_STATEMENT_FILE", "Yüklenen ekstre dosyası boş.");
        }

        if (request.FileContent.Length > MaxFileSizeBytes)
        {
            throw new DomainException(
                "STATEMENT_FILE_TOO_LARGE",
                "Ekstre dosyası en fazla 10 MB olabilir.");
        }

        var wallet = await _dbContext.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(
                w => w.Id == request.WalletId && w.WorkspaceId == request.WorkspaceId && !w.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException("WALLET_NOT_FOUND", "Cüzdan bu çalışma alanında bulunamadı.");

        if (wallet.IsArchived)
        {
            throw new DomainException("WALLET_ARCHIVED", "Arşivlenmiş cüzdana ekstre içe aktarılamaz.");
        }

        var hasPdfMagic = BankStatementParser.HasPdfMagicBytes(request.FileContent);
        _logger?.LogDebug(
            "Statement import preview: FormatHint='{ContentType}', Length={Length}, HasPdfMagicBytes={HasPdfMagic}",
            request.ContentType ?? "(none)",
            request.FileContent.Length,
            hasPdfMagic);

        var parsedResult = BankStatementParser.Parse(
            request.FileName,
            request.FileContent,
            request.ContentType,
            onPdfLinesExtracted: lines =>
            {
                _logger?.LogDebug(
                    "PDF statement reconstructed {LineCount} visual lines.",
                    lines.Count);
            });
        if (parsedResult.Rows.Count == 0)
        {
            throw new DomainException(
                "NO_TRANSACTIONS_PARSED",
                "Ekstre dosyasında içe aktarılabilir işlem satırı bulunamadı.");
        }

        var workspaceCategories = await _dbContext.Categories
            .AsNoTracking()
            .Where(c => c.WorkspaceId == request.WorkspaceId && !c.IsDeleted)
            .OrderBy(c => c.NameTr)
            .ToListAsync(cancellationToken);

        var minDate = parsedResult.Rows.Min(r => r.TransactionDate);
        var maxDate = parsedResult.Rows.Max(r => r.TransactionDate);

        var existingRows = await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.WorkspaceId == request.WorkspaceId &&
                        t.WalletId == request.WalletId &&
                        !t.IsDeleted &&
                        t.TransactionDate >= minDate &&
                        t.TransactionDate <= maxDate)
            .Select(t => new { t.TransactionDate, t.Type, t.Amount })
            .ToListAsync(cancellationToken);

        var existingCounts = new Dictionary<(DateOnly Date, TransactionType Type, decimal Amount), int>();
        foreach (var ex in existingRows)
        {
            var key = (ex.TransactionDate, ex.Type, decimal.Round(ex.Amount, 2, MidpointRounding.AwayFromZero));
            existingCounts[key] = existingCounts.GetValueOrDefault(key) + 1;
        }

        var isEnglish = CategoryLocalization.IsEnglish(_currentUserService.Locale);
        var previewItems = new List<ImportPreviewItemDto>(parsedResult.Rows.Count);
        decimal totalIncome = 0m;
        decimal totalExpense = 0m;
        var duplicateCount = 0;

        for (var i = 0; i < parsedResult.Rows.Count; i++)
        {
            var row = parsedResult.Rows[i];
            var txType = row.SignedAmount >= 0m ? TransactionType.Income : TransactionType.Expense;
            var absAmount = decimal.Round(Math.Abs(row.SignedAmount), 2, MidpointRounding.AwayFromZero);

            var dupKey = (row.TransactionDate, txType, absAmount);
            var isDuplicate = false;
            if (existingCounts.TryGetValue(dupKey, out var remaining) && remaining > 0)
            {
                isDuplicate = true;
                existingCounts[dupKey] = remaining - 1;
                duplicateCount++;
            }

            var category = StatementAutoCategorizer.Categorize(row.Description, txType, workspaceCategories);

            if (txType == TransactionType.Income) totalIncome += absAmount;
            else totalExpense += absAmount;

            previewItems.Add(new ImportPreviewItemDto(
                RowIndex: i,
                TransactionDate: row.TransactionDate,
                Description: row.Description,
                Type: txType,
                Amount: absAmount,
                CategoryId: category?.Id,
                CategoryName: category is null
                    ? null
                    : (isEnglish && category.NameEn is not null ? category.NameEn : category.NameTr),
                CategoryIcon: category?.Icon,
                CategoryColorHex: category?.ColorHex,
                IsDuplicate: isDuplicate));
        }

        return new ImportPreviewResultDto(
            WalletId: wallet.Id,
            WalletName: wallet.Name,
            CurrencyCode: wallet.CurrencyCode,
            FileName: request.FileName,
            DetectedFormat: parsedResult.DetectedFormat,
            TotalRows: previewItems.Count,
            DuplicateCount: duplicateCount,
            TotalIncome: decimal.Round(totalIncome, 2, MidpointRounding.AwayFromZero),
            TotalExpense: decimal.Round(totalExpense, 2, MidpointRounding.AwayFromZero),
            Items: previewItems);
    }
}

// ───────────────────────────── Confirm ─────────────────────────────

public sealed record ConfirmStatementImportCommand(
    Guid WorkspaceId,
    Guid WalletId,
    IReadOnlyList<ConfirmImportItemInput> Items) : IRequest<ConfirmImportResultDto>;

public sealed class ConfirmStatementImportCommandValidator : AbstractValidator<ConfirmStatementImportCommand>
{
    public ConfirmStatementImportCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.WalletId).NotEmpty().WithMessage("Cüzdan kimliği zorunludur.");
        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("İçe aktarılacak en az bir işlem seçilmelidir.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Amount)
                .GreaterThan(0m).WithMessage("İşlem tutarı sıfırdan büyük olmalıdır.");
            item.RuleFor(i => i.CategoryId)
                .NotEmpty().WithMessage("Her işlem için bir kategori seçilmelidir.");
            item.RuleFor(i => i.Type)
                .Must(t => t is TransactionType.Income or TransactionType.Expense)
                .WithMessage("İçe aktarılan işlemler yalnızca Gelir veya Gider olabilir.");
            item.RuleFor(i => i.Description)
                .MaximumLength(200);
        });
    }
}

public sealed class ConfirmStatementImportCommandHandler
    : IRequestHandler<ConfirmStatementImportCommand, ConfirmImportResultDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ConfirmStatementImportCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<ConfirmImportResultDto> Handle(
        ConfirmStatementImportCommand request,
        CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Contributor,
            cancellationToken);

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new DomainException("EMPTY_IMPORT_LIST", "İçe aktarılacak en az bir işlem seçilmelidir.");
        }

        var wallet = await _dbContext.Wallets
            .FirstOrDefaultAsync(
                w => w.Id == request.WalletId && w.WorkspaceId == request.WorkspaceId && !w.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException("WALLET_NOT_FOUND", "Cüzdan bu çalışma alanında bulunamadı.");

        if (wallet.IsArchived)
        {
            throw new DomainException("WALLET_ARCHIVED", "Arşivlenmiş cüzdana ekstre içe aktarılamaz.");
        }

        var requestedCategoryIds = request.Items.Select(i => i.CategoryId).Distinct().ToList();
        var categoriesById = await _dbContext.Categories
            .AsNoTracking()
            .Where(c => c.WorkspaceId == request.WorkspaceId && !c.IsDeleted && requestedCategoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var now = _dateTimeProvider.UtcNow;
        decimal totalIncome = 0m;
        decimal totalExpense = 0m;

        foreach (var item in request.Items)
        {
            if (item.Type is not (TransactionType.Income or TransactionType.Expense))
            {
                throw new DomainException(
                    "INVALID_IMPORT_TRANSACTION_TYPE",
                    "İçe aktarılan işlemler yalnızca Gelir veya Gider olabilir.");
            }

            if (!categoriesById.TryGetValue(item.CategoryId, out var category))
            {
                throw new NotFoundException("CATEGORY_NOT_FOUND", "Seçilen kategori bu çalışma alanında bulunamadı.");
            }

            var expectedCategoryType = item.Type == TransactionType.Income
                ? CategoryType.Income
                : CategoryType.Expense;

            if (category.Type != expectedCategoryType)
            {
                throw new DomainException(
                    "CATEGORY_TYPE_MISMATCH",
                    "Seçilen kategori işlem türü ile uyuşmuyor.");
            }

            var merchant = string.IsNullOrWhiteSpace(item.Description)
                ? null
                : (item.Description.Trim().Length > 200 ? item.Description.Trim()[..200] : item.Description.Trim());

            var tx = Transaction.Create(
                workspaceId: request.WorkspaceId,
                walletId: wallet.Id,
                type: item.Type,
                amount: item.Amount,
                transactionDate: item.TransactionDate,
                categoryId: item.CategoryId,
                counterpartyWalletId: null,
                receiptId: null,
                currencyCode: wallet.CurrencyCode,
                merchantName: merchant,
                notes: "Ekstre İçe Aktarım",
                createdByUserId: access.UserId,
                paidByUserId: access.UserId,
                idempotencyKey: null,
                utcNow: now);

            tx.ApplyToWallets(wallet, null);
            _dbContext.Transactions.Add(tx);

            if (item.Type == TransactionType.Income) totalIncome += tx.Amount;
            else totalExpense += tx.Amount;
        }

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId,
                access.UserId,
                "StatementImported",
                nameof(Wallet),
                wallet.Id,
                oldValuesJson: null,
                newValuesJson: AuditJson.Serialize(new
                {
                    ImportedCount = request.Items.Count,
                    TotalIncome = totalIncome,
                    TotalExpense = totalExpense,
                    UpdatedWalletBalance = wallet.CurrentBalance
                }),
                utcNow: now));
        }

        // Single atomic SaveChangesAsync commits all transactions + wallet balance update together
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ConfirmImportResultDto(
            WalletId: wallet.Id,
            ImportedCount: request.Items.Count,
            TotalIncome: decimal.Round(totalIncome, 2, MidpointRounding.AwayFromZero),
            TotalExpense: decimal.Round(totalExpense, 2, MidpointRounding.AwayFromZero),
            UpdatedWalletBalance: wallet.CurrentBalance);
    }
}
