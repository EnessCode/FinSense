using FinSense.Application.Common.Auditing;
using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Investments.Contracts;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Investments.Commands;

// ───────────────────────────── Add Lot ─────────────────────────────

public sealed record AddInvestmentLotCommand(
    Guid WorkspaceId,
    InvestmentAssetCategory Category,
    string Symbol,
    string AssetName,
    decimal Quantity,
    decimal UnitPrice,
    decimal? TotalCost = null,
    decimal? CurrentUnitPrice = null,
    DateTime? PurchaseDateUtc = null,
    string? Notes = null) : IRequest<InvestmentLotDto>;

public sealed class AddInvestmentLotCommandValidator : AbstractValidator<AddInvestmentLotCommand>
{
    public AddInvestmentLotCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.Category).IsInEnum().WithMessage("Geçersiz yatırım varlık kategorisi.");

        RuleFor(x => x.Symbol)
            .NotEmpty().WithMessage("Varlık sembolü zorunludur.")
            .MaximumLength(24).WithMessage("Varlık sembolü en fazla 24 karakter olabilir.");

        RuleFor(x => x.AssetName)
            .NotEmpty().WithMessage("Varlık adı zorunludur.")
            .MaximumLength(100).WithMessage("Varlık adı en fazla 100 karakter olabilir.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0m).WithMessage("Miktar sıfırdan büyük olmalıdır.")
            .PrecisionScale(18, 6, ignoreTrailingZeros: true)
            .WithMessage("Miktar en fazla 6 ondalık basamak içerebilir.");

        RuleFor(x => x.UnitPrice)
            .GreaterThan(0m).WithMessage("Birim alış fiyatı sıfırdan büyük olmalıdır.")
            .PrecisionScale(18, 4, ignoreTrailingZeros: true)
            .WithMessage("Birim alış fiyatı en fazla 4 ondalık basamak içerebilir.");

        RuleFor(x => x.TotalCost!.Value)
            .GreaterThanOrEqualTo(0m).WithMessage("Toplam maliyet negatif olamaz.")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .When(x => x.TotalCost.HasValue);

        RuleFor(x => x.CurrentUnitPrice!.Value)
            .GreaterThan(0m).WithMessage("Güncel birim fiyat sıfırdan büyük olmalıdır.")
            .PrecisionScale(18, 4, ignoreTrailingZeros: true)
            .When(x => x.CurrentUnitPrice.HasValue);

        RuleFor(x => x.Notes).MaximumLength(250);
    }
}

public sealed class AddInvestmentLotCommandHandler
    : IRequestHandler<AddInvestmentLotCommand, InvestmentLotDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AddInvestmentLotCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<InvestmentLotDto> Handle(
        AddInvestmentLotCommand request,
        CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Contributor,
            cancellationToken);

        var now = _dateTimeProvider.UtcNow;
        var normalizedSymbol = request.Symbol.Trim().ToUpperInvariant();

        var existingSymbolLots = await _dbContext.InvestmentTransactions
            .Where(i => i.WorkspaceId == request.WorkspaceId && i.Symbol == normalizedSymbol)
            .ToListAsync(cancellationToken);

        decimal resolvedCurrentUnitPrice;
        if (request.CurrentUnitPrice.HasValue)
        {
            resolvedCurrentUnitPrice = request.CurrentUnitPrice.Value;
            foreach (var existingLot in existingSymbolLots)
            {
                existingLot.UpdateCurrentUnitPrice(resolvedCurrentUnitPrice);
            }
        }
        else if (existingSymbolLots.Count > 0)
        {
            resolvedCurrentUnitPrice = existingSymbolLots
                .OrderByDescending(l => l.CreatedAtUtc)
                .First()
                .CurrentUnitPrice;
        }
        else
        {
            resolvedCurrentUnitPrice = request.UnitPrice;
        }

        var currencyCode = await _dbContext.Workspaces
            .AsNoTracking()
            .Where(w => w.Id == request.WorkspaceId)
            .Select(w => w.DefaultCurrency)
            .FirstOrDefaultAsync(cancellationToken) ?? "TRY";

        var deterministicTotalCost = Math.Round(
            request.Quantity * request.UnitPrice,
            2,
            MidpointRounding.AwayFromZero);

        var lot = InvestmentTransaction.Create(
            workspaceId: request.WorkspaceId,
            createdByUserId: access.UserId,
            category: request.Category,
            symbol: normalizedSymbol,
            assetName: request.AssetName,
            quantity: request.Quantity,
            unitPrice: request.UnitPrice,
            totalCost: deterministicTotalCost,
            currentUnitPrice: resolvedCurrentUnitPrice,
            purchaseDateUtc: request.PurchaseDateUtc ?? now,
            notes: request.Notes,
            currency: currencyCode,
            utcNow: now);

        _dbContext.InvestmentTransactions.Add(lot);

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId,
                access.UserId,
                "InvestmentLotAdded",
                nameof(InvestmentTransaction),
                lot.Id,
                oldValuesJson: null,
                newValuesJson: AuditJson.Serialize(new
                {
                    lot.Category,
                    lot.Symbol,
                    lot.AssetName,
                    lot.Quantity,
                    lot.UnitPrice,
                    lot.TotalCost,
                    lot.CurrentUnitPrice
                }),
                utcNow: now));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new InvestmentLotDto(
            lot.Id,
            lot.Quantity,
            lot.UnitPrice,
            lot.TotalCost,
            lot.PurchaseDateUtc,
            lot.Notes);
    }
}

// ───────────────────────────── Update Current Price ─────────────────────────────

public sealed record UpdateAssetCurrentPriceCommand(
    Guid WorkspaceId,
    string Symbol,
    decimal CurrentUnitPrice) : IRequest<UpdateAssetPriceResultDto>;

public sealed class UpdateAssetCurrentPriceCommandValidator
    : AbstractValidator<UpdateAssetCurrentPriceCommand>
{
    public UpdateAssetCurrentPriceCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();

        RuleFor(x => x.Symbol)
            .NotEmpty().WithMessage("Varlık sembolü zorunludur.")
            .MaximumLength(24);

        RuleFor(x => x.CurrentUnitPrice)
            .GreaterThan(0m).WithMessage("Güncel birim fiyat sıfırdan büyük olmalıdır.")
            .PrecisionScale(18, 4, ignoreTrailingZeros: true)
            .WithMessage("Güncel birim fiyat en fazla 4 ondalık basamak içerebilir.");
    }
}

public sealed class UpdateAssetCurrentPriceCommandHandler
    : IRequestHandler<UpdateAssetCurrentPriceCommand, UpdateAssetPriceResultDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateAssetCurrentPriceCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<UpdateAssetPriceResultDto> Handle(
        UpdateAssetCurrentPriceCommand request,
        CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Contributor,
            cancellationToken);

        var normalizedSymbol = request.Symbol.Trim().ToUpperInvariant();

        var lots = await _dbContext.InvestmentTransactions
            .Where(i => i.WorkspaceId == request.WorkspaceId && i.Symbol == normalizedSymbol)
            .ToListAsync(cancellationToken);

        if (lots.Count == 0)
        {
            throw new NotFoundException(
                "INVESTMENT_ASSET_NOT_FOUND",
                "Bu çalışma alanında belirtilen sembole ait yatırım varlığı bulunamadı.");
        }

        var previousPrice = lots[0].CurrentUnitPrice;
        foreach (var lot in lots)
        {
            lot.UpdateCurrentUnitPrice(request.CurrentUnitPrice);
        }

        var updatedPrice = lots[0].CurrentUnitPrice;

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId,
                access.UserId,
                "InvestmentPriceUpdated",
                nameof(InvestmentTransaction),
                lots[0].Id,
                oldValuesJson: AuditJson.Serialize(new { Symbol = normalizedSymbol, CurrentUnitPrice = previousPrice }),
                newValuesJson: AuditJson.Serialize(new { Symbol = normalizedSymbol, CurrentUnitPrice = updatedPrice }),
                utcNow: _dateTimeProvider.UtcNow));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new UpdateAssetPriceResultDto(normalizedSymbol, updatedPrice, lots.Count);
    }
}

// ───────────────────────────── Delete Lot ─────────────────────────────

public sealed record DeleteInvestmentLotCommand(Guid WorkspaceId, Guid LotId) : IRequest;

public sealed class DeleteInvestmentLotCommandHandler : IRequestHandler<DeleteInvestmentLotCommand>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly IDateTimeProvider _dateTimeProvider;

    public DeleteInvestmentLotCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task Handle(DeleteInvestmentLotCommand request, CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Contributor,
            cancellationToken);

        var lot = await _dbContext.InvestmentTransactions
            .FirstOrDefaultAsync(
                i => i.Id == request.LotId && i.WorkspaceId == request.WorkspaceId,
                cancellationToken)
            ?? throw new NotFoundException(
                "INVESTMENT_LOT_NOT_FOUND",
                "Yatırım alım kaydı bu çalışma alanında bulunamadı.");

        if (!access.Role.CanMutateOtherMembersTransactions() && lot.CreatedByUserId != access.UserId)
        {
            throw new ForbiddenAccessException(
                "INVESTMENT_LOT_FORBIDDEN",
                "Katkıda Bulunan (Contributor) rolündeki üyeler yalnızca kendi ekledikleri yatırım kayıtlarını silebilir.");
        }

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId,
                access.UserId,
                "InvestmentLotDeleted",
                nameof(InvestmentTransaction),
                lot.Id,
                oldValuesJson: AuditJson.Serialize(new
                {
                    lot.Category,
                    lot.Symbol,
                    lot.Quantity,
                    lot.TotalCost
                }),
                newValuesJson: null,
                utcNow: _dateTimeProvider.UtcNow));
        }

        _dbContext.InvestmentTransactions.Remove(lot);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
