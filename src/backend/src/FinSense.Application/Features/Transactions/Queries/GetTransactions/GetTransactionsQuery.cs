using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Models;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Categories.Contracts;
using FinSense.Application.Features.Transactions.Contracts;
using FinSense.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Transactions.Queries.GetTransactions;

public sealed record GetTransactionsQuery(
    Guid WorkspaceId,
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    Guid? WalletId = null,
    Guid? CategoryId = null,
    TransactionType? Type = null,
    Guid? PaidByUserId = null,
    string? Search = null,
    int PageNumber = 1,
    int PageSize = 20) : IRequest<PaginatedList<TransactionDto>>;

public sealed class GetTransactionsQueryValidator : AbstractValidator<GetTransactionsQuery>
{
    public GetTransactionsQueryValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();

        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Sayfa numarası en az 1 olmalıdır.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("Sayfa boyutu 1 ile 100 arasında olmalıdır.");

        RuleFor(x => x)
            .Must(x => !x.DateFrom.HasValue || !x.DateTo.HasValue || x.DateFrom.Value <= x.DateTo.Value)
            .WithMessage("Başlangıç tarihi bitiş tarihinden büyük olamaz.");
    }
}

public sealed class GetTransactionsQueryHandler
    : IRequestHandler<GetTransactionsQuery, PaginatedList<TransactionDto>>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly ICurrentUserService _currentUserService;

    public GetTransactionsQueryHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _currentUserService = currentUserService;
    }

    public async Task<PaginatedList<TransactionDto>> Handle(
        GetTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        await _workspaceAccess.EnsureAccessAsync(request.WorkspaceId, WorkspaceRole.Viewer, cancellationToken);
        var isEnglish = CategoryLocalization.IsEnglish(_currentUserService.Locale);

        var query = _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.WorkspaceId == request.WorkspaceId && !t.IsDeleted);

        if (request.DateFrom.HasValue)
        {
            query = query.Where(t => t.TransactionDate >= request.DateFrom.Value);
        }

        if (request.DateTo.HasValue)
        {
            query = query.Where(t => t.TransactionDate <= request.DateTo.Value);
        }

        if (request.WalletId.HasValue)
        {
            query = query.Where(t =>
                t.WalletId == request.WalletId.Value ||
                t.CounterpartyWalletId == request.WalletId.Value);
        }

        if (request.CategoryId.HasValue)
        {
            query = query.Where(t => t.CategoryId == request.CategoryId.Value);
        }

        if (request.Type.HasValue)
        {
            query = query.Where(t => t.Type == request.Type.Value);
        }

        if (request.PaidByUserId.HasValue)
        {
            query = query.Where(t => t.PaidByUserId == request.PaidByUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(t =>
                (t.MerchantName != null && t.MerchantName.ToLower().Contains(term)) ||
                (t.Notes != null && t.Notes.ToLower().Contains(term)));
        }

        var projected = query
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.CreatedAtUtc)
            .Select(t => new TransactionDto(
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
                t.ReceiptId,
                t.Type,
                t.Amount,
                t.CurrencyCode,
                t.TransactionDate,
                t.MerchantName,
                t.Notes,
                t.CreatedByUserId,
                t.CreatedByUser != null ? t.CreatedByUser.DisplayName : null,
                t.PaidByUserId,
                t.PaidByUser != null ? t.PaidByUser.DisplayName : null,
                t.CreatedAtUtc));

        return await PaginatedList<TransactionDto>.CreateAsync(
            projected,
            request.PageNumber,
            request.PageSize,
            cancellationToken);
    }
}
