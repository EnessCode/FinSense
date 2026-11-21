using FinSense.Application.Common.Auditing;
using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Wallets.Contracts;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Wallets.Commands.UpdateWallet;

/// <summary>
/// Updates wallet metadata. <c>CurrentBalance</c> is never accepted from the client: if <see cref="InitialBalance"/>
/// changes, the current balance is shifted by the exact delta and an auditable <c>WalletBalanceAdjusted</c> entry is written.
/// </summary>
public sealed record UpdateWalletCommand(
    Guid WorkspaceId,
    Guid WalletId,
    string Name,
    decimal? CreditLimit,
    string? Icon,
    string? ColorHex,
    bool IsArchived,
    decimal? InitialBalance = null) : IRequest<WalletDto>;

public sealed class UpdateWalletCommandValidator : AbstractValidator<UpdateWalletCommand>
{
    public UpdateWalletCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.WalletId).NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Cüzdan adı zorunludur.")
            .MaximumLength(100).WithMessage("Cüzdan adı en fazla 100 karakter olabilir.");

        RuleFor(x => x.CreditLimit)
            .GreaterThanOrEqualTo(0m).When(x => x.CreditLimit.HasValue)
            .WithMessage("Kredi limiti negatif olamaz.");

        RuleFor(x => x.InitialBalance!.Value)
            .PrecisionScale(19, 4, ignoreTrailingZeros: true)
            .When(x => x.InitialBalance.HasValue)
            .WithMessage("Başlangıç bakiyesi NUMERIC(19,4) hassasiyetini aşamaz.");

        RuleFor(x => x.Icon).MaximumLength(50);

        RuleFor(x => x.ColorHex)
            .Matches("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$").When(x => !string.IsNullOrWhiteSpace(x.ColorHex))
            .WithMessage("Renk kodu #RRGGBB veya #RRGGBBAA formatında olmalıdır.");
    }
}

public sealed class UpdateWalletCommandHandler : IRequestHandler<UpdateWalletCommand, WalletDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateWalletCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<WalletDto> Handle(UpdateWalletCommand request, CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(request.WorkspaceId, WorkspaceRole.Admin, cancellationToken);
        var now = _dateTimeProvider.UtcNow;

        var wallet = await _dbContext.Wallets
            .FirstOrDefaultAsync(
                w => w.Id == request.WalletId && w.WorkspaceId == request.WorkspaceId && !w.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException("WALLET_NOT_FOUND", "Cüzdan bu çalışma alanında bulunamadı.");

        if (request.CreditLimit.HasValue && wallet.Type != WalletType.CreditCard)
        {
            throw new ApplicationValidationException(
            [
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.CreditLimit),
                    "Kredi limiti yalnızca kredi kartı cüzdanları için tanımlanabilir.")
            ]);
        }

        var before = new
        {
            wallet.Name,
            wallet.CreditLimit,
            wallet.Icon,
            wallet.ColorHex,
            wallet.IsArchived,
            wallet.InitialBalance,
            wallet.CurrentBalance
        };

        wallet.UpdateMetadata(request.Name, request.CreditLimit, request.Icon, request.ColorHex, request.IsArchived);

        if (request.InitialBalance.HasValue && request.InitialBalance.Value != wallet.InitialBalance)
        {
            var previousCurrent = wallet.CurrentBalance;
            var previousInitial = wallet.InitialBalance;
            var delta = wallet.AdjustInitialBalance(request.InitialBalance.Value);

            // Balance adjustments are always audited (Personal and Household) per DATABASE-DESIGN wallet mutation rule.
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId,
                access.UserId,
                "WalletBalanceAdjusted",
                nameof(Wallet),
                wallet.Id,
                oldValuesJson: AuditJson.Serialize(new { InitialBalance = previousInitial, CurrentBalance = previousCurrent }),
                newValuesJson: AuditJson.Serialize(new { wallet.InitialBalance, wallet.CurrentBalance, Delta = delta }),
                utcNow: now));
        }

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId,
                access.UserId,
                "WalletUpdated",
                nameof(Wallet),
                wallet.Id,
                oldValuesJson: AuditJson.Serialize(before),
                newValuesJson: AuditJson.Serialize(new
                {
                    wallet.Name,
                    wallet.CreditLimit,
                    wallet.Icon,
                    wallet.ColorHex,
                    wallet.IsArchived,
                    wallet.InitialBalance,
                    wallet.CurrentBalance
                }),
                utcNow: now));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return WalletMappings.ToDto(wallet);
    }
}
