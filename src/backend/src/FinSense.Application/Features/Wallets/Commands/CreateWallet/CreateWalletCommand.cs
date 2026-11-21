using FinSense.Application.Common.Auditing;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Wallets.Contracts;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Wallets.Commands.CreateWallet;

public sealed record CreateWalletCommand(
    Guid WorkspaceId,
    string Name,
    WalletType Type,
    decimal InitialBalance = 0m,
    string? CurrencyCode = null,
    decimal? CreditLimit = null,
    string? Icon = null,
    string? ColorHex = null) : IRequest<WalletDto>;

public sealed class CreateWalletCommandValidator : AbstractValidator<CreateWalletCommand>
{
    public CreateWalletCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Cüzdan adı zorunludur.")
            .MaximumLength(100).WithMessage("Cüzdan adı en fazla 100 karakter olabilir.");

        RuleFor(x => x.Type).IsInEnum().WithMessage("Geçersiz cüzdan türü.");

        RuleFor(x => x.InitialBalance)
            .PrecisionScale(19, 4, ignoreTrailingZeros: true)
            .WithMessage("Başlangıç bakiyesi NUMERIC(19,4) hassasiyetini aşamaz.");

        RuleFor(x => x.CurrencyCode)
            .Length(3).When(x => !string.IsNullOrWhiteSpace(x.CurrencyCode))
            .WithMessage("Para birimi kodu 3 karakterli ISO 4217 formatında olmalıdır.");

        RuleFor(x => x.CreditLimit)
            .GreaterThanOrEqualTo(0m).When(x => x.CreditLimit.HasValue)
            .WithMessage("Kredi limiti negatif olamaz.");

        RuleFor(x => x.CreditLimit)
            .Null().When(x => x.Type != WalletType.CreditCard)
            .WithMessage("Kredi limiti yalnızca kredi kartı cüzdanları için tanımlanabilir.");

        RuleFor(x => x.Icon).MaximumLength(50);

        RuleFor(x => x.ColorHex)
            .Matches("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$").When(x => !string.IsNullOrWhiteSpace(x.ColorHex))
            .WithMessage("Renk kodu #RRGGBB veya #RRGGBBAA formatında olmalıdır.");
    }
}

public sealed class CreateWalletCommandHandler : IRequestHandler<CreateWalletCommand, WalletDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateWalletCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<WalletDto> Handle(CreateWalletCommand request, CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(request.WorkspaceId, WorkspaceRole.Admin, cancellationToken);

        var currencyCode = request.CurrencyCode;
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            currencyCode = await _dbContext.Workspaces
                .AsNoTracking()
                .Where(w => w.Id == request.WorkspaceId)
                .Select(w => w.DefaultCurrency)
                .FirstAsync(cancellationToken);
        }

        var wallet = Wallet.Create(
            request.WorkspaceId,
            request.Name,
            request.Type,
            request.InitialBalance,
            currencyCode,
            request.CreditLimit,
            request.Icon,
            request.ColorHex);

        _dbContext.Wallets.Add(wallet);

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId,
                access.UserId,
                "WalletCreated",
                nameof(Wallet),
                wallet.Id,
                oldValuesJson: null,
                newValuesJson: AuditJson.Serialize(new
                {
                    wallet.Name,
                    wallet.Type,
                    wallet.CurrencyCode,
                    wallet.InitialBalance,
                    wallet.CreditLimit
                }),
                utcNow: _dateTimeProvider.UtcNow));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return WalletMappings.ToDto(wallet);
    }
}
