using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Ai.Contracts;
using FinSense.Application.Features.Categories.Contracts;
using FinSense.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Ai.Commands.ScanReceipt;

public sealed record ScanReceiptCommand(
    Guid WorkspaceId,
    string? ImageBase64,
    string? RawOcrText,
    string? FileName = null,
    string? MimeType = null) : IRequest<ReceiptScanResultDto>;

public sealed class ScanReceiptCommandValidator : AbstractValidator<ScanReceiptCommand>
{
    public ScanReceiptCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();

        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.ImageBase64) || !string.IsNullOrWhiteSpace(x.RawOcrText))
            .WithMessage("Fiş görüntüsü (imageBase64 / file) veya OCR metni (rawOcrText) sağlanmalıdır.");
    }
}

public sealed class ScanReceiptCommandHandler
    : IRequestHandler<ScanReceiptCommand, ReceiptScanResultDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAiQuotaService _aiQuotaService;
    private readonly IAiServiceClient _aiServiceClient;

    public ScanReceiptCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        ICurrentUserService currentUserService,
        IAiQuotaService aiQuotaService,
        IAiServiceClient aiServiceClient)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _currentUserService = currentUserService;
        _aiQuotaService = aiQuotaService;
        _aiServiceClient = aiServiceClient;
    }

    public async Task<ReceiptScanResultDto> Handle(
        ScanReceiptCommand request,
        CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Contributor,
            cancellationToken);

        var remainingQuota = _aiQuotaService.ConsumeDailyQuota(access.UserId, AiQuotaType.ReceiptScan);

        var locale = _currentUserService.Locale;
        var isEnglish = CategoryLocalization.IsEnglish(locale);

        var expenseCategories = await _dbContext.Categories
            .AsNoTracking()
            .Where(c => c.WorkspaceId == request.WorkspaceId &&
                        !c.IsDeleted &&
                        c.Type == CategoryType.Expense)
            .OrderByDescending(c => c.IsSystem)
            .ThenBy(c => c.NameTr)
            .Select(c => new
            {
                c.Id,
                c.NameTr,
                c.NameEn,
                LocalizedName = isEnglish && c.NameEn != null ? c.NameEn : c.NameTr
            })
            .ToListAsync(cancellationToken);

        var availableCategoryNames = expenseCategories
            .Select(c => c.LocalizedName)
            .Distinct()
            .ToList();

        var internalRequest = new InternalReceiptOcrRequest(
            request.ImageBase64,
            request.RawOcrText,
            locale,
            availableCategoryNames,
            request.MimeType);

        var ocrResult = await _aiServiceClient.ExtractReceiptAsync(internalRequest, cancellationToken);

        Guid? suggestedCategoryId = null;
        if (ocrResult.IsValidReceipt && !string.IsNullOrWhiteSpace(ocrResult.SuggestedCategory))
        {
            var suggested = ocrResult.SuggestedCategory.Trim();
            var normalizedSuggested = NormalizeCategoryName(suggested);

            var matchedCategory = expenseCategories.FirstOrDefault(c =>
                string.Equals(c.NameTr, suggested, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(c.NameEn) &&
                 string.Equals(c.NameEn, suggested, StringComparison.OrdinalIgnoreCase)) ||
                NormalizeCategoryName(c.NameTr) == normalizedSuggested ||
                (!string.IsNullOrWhiteSpace(c.NameEn) &&
                 NormalizeCategoryName(c.NameEn) == normalizedSuggested));

            suggestedCategoryId = matchedCategory?.Id;
        }

        return new ReceiptScanResultDto(
            ocrResult.IsValidReceipt,
            ocrResult.RejectionReason,
            ocrResult.MerchantName,
            ocrResult.TransactionDate,
            ocrResult.TotalAmount,
            ocrResult.TaxAmount,
            ocrResult.Currency,
            ocrResult.SuggestedCategory,
            suggestedCategoryId,
            ocrResult.Confidence,
            ocrResult.ProviderUsed,
            remainingQuota);
    }

    private static string NormalizeCategoryName(string value)
    {
        return value
            .Trim()
            .ToLowerInvariant()
            .Replace("&", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty);
    }
}
