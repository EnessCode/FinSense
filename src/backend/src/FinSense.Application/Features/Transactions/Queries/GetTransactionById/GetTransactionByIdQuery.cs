using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Categories.Contracts;
using FinSense.Application.Features.Transactions.Contracts;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Transactions.Queries.GetTransactionById;

public sealed record GetTransactionByIdQuery(Guid WorkspaceId, Guid TransactionId) : IRequest<TransactionDetailDto>;

public sealed class GetTransactionByIdQueryHandler : IRequestHandler<GetTransactionByIdQuery, TransactionDetailDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly ICurrentUserService _currentUserService;

    public GetTransactionByIdQueryHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _currentUserService = currentUserService;
    }

    public async Task<TransactionDetailDto> Handle(
        GetTransactionByIdQuery request,
        CancellationToken cancellationToken)
    {
        await _workspaceAccess.EnsureAccessAsync(request.WorkspaceId, WorkspaceRole.Viewer, cancellationToken);
        var isEnglish = CategoryLocalization.IsEnglish(_currentUserService.Locale);

        var dto = await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.Id == request.TransactionId && t.WorkspaceId == request.WorkspaceId && !t.IsDeleted)
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
            .FirstOrDefaultAsync(cancellationToken);

        return dto ?? throw new NotFoundException(
            "TRANSACTION_NOT_FOUND",
            "İşlem bu çalışma alanında bulunamadı.");
    }
}
