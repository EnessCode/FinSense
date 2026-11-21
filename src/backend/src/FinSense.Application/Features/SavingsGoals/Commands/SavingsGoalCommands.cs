using FinSense.Application.Common.Auditing;
using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.SavingsGoals.Contracts;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.SavingsGoals.Commands;

// ───────────────────────────── Create ─────────────────────────────

public sealed record CreateSavingsGoalCommand(
    Guid WorkspaceId,
    string Name,
    decimal TargetAmount,
    decimal CurrentAmount = 0m,
    Guid? LinkedWalletId = null,
    DateOnly? TargetDate = null) : IRequest<SavingsGoalDto>;

public sealed class CreateSavingsGoalCommandValidator : AbstractValidator<CreateSavingsGoalCommand>
{
    public CreateSavingsGoalCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Birikim hedefi adı zorunludur.")
            .MaximumLength(150).WithMessage("Birikim hedefi adı en fazla 150 karakter olabilir.");

        RuleFor(x => x.TargetAmount)
            .GreaterThan(0m).WithMessage("Hedef tutar sıfırdan büyük olmalıdır.")
            .PrecisionScale(19, 4, ignoreTrailingZeros: true)
            .WithMessage("Hedef tutar NUMERIC(19,4) hassasiyetini aşamaz.");

        RuleFor(x => x.CurrentAmount)
            .GreaterThanOrEqualTo(0m).WithMessage("Mevcut birikim tutarı negatif olamaz.")
            .PrecisionScale(19, 4, ignoreTrailingZeros: true)
            .WithMessage("Mevcut birikim tutarı NUMERIC(19,4) hassasiyetini aşamaz.");
    }
}

public sealed class CreateSavingsGoalCommandHandler
    : IRequestHandler<CreateSavingsGoalCommand, SavingsGoalDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateSavingsGoalCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<SavingsGoalDto> Handle(
        CreateSavingsGoalCommand request,
        CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(request.WorkspaceId, WorkspaceRole.Admin, cancellationToken);

        Wallet? linkedWallet = null;
        if (request.LinkedWalletId.HasValue)
        {
            if (request.CurrentAmount > 0m)
            {
                throw new DomainException(
                    "LINKED_WALLET_GOAL_MANUAL_AMOUNT_FORBIDDEN",
                    "Bir cüzdana bağlı birikim hedeflerinde mevcut tutar cüzdan bakiyesinden otomatik hesaplanır.");
            }

            linkedWallet = await _dbContext.Wallets
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    w => w.Id == request.LinkedWalletId.Value && w.WorkspaceId == request.WorkspaceId && !w.IsDeleted,
                    cancellationToken)
                ?? throw new NotFoundException("WALLET_NOT_FOUND", "Bağlanacak cüzdan bu çalışma alanında bulunamadı.");
        }

        var currencyCode = linkedWallet?.CurrencyCode ?? await _dbContext.Workspaces
            .AsNoTracking()
            .Where(w => w.Id == request.WorkspaceId)
            .Select(w => w.DefaultCurrency)
            .FirstAsync(cancellationToken);

        var goal = SavingsGoal.Create(
            request.WorkspaceId,
            request.Name,
            request.TargetAmount,
            request.LinkedWalletId.HasValue ? 0m : request.CurrentAmount,
            request.LinkedWalletId,
            request.TargetDate,
            currencyCode);

        _dbContext.SavingsGoals.Add(goal);

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId,
                access.UserId,
                "SavingsGoalCreated",
                nameof(SavingsGoal),
                goal.Id,
                oldValuesJson: null,
                newValuesJson: AuditJson.Serialize(new
                {
                    goal.Name,
                    goal.TargetAmount,
                    goal.CurrentAmount,
                    goal.LinkedWalletId,
                    goal.TargetDate
                }),
                utcNow: _dateTimeProvider.UtcNow));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var effectiveCurrent = linkedWallet?.CurrentBalance ?? goal.CurrentAmount;

        return SavingsGoalMapper.ToDto(
            goal.Id,
            goal.WorkspaceId,
            goal.LinkedWalletId,
            linkedWallet?.Name,
            goal.Name,
            goal.TargetAmount,
            effectiveCurrent,
            goal.CurrencyCode,
            goal.TargetDate);
    }
}

// ───────────────────────────── Update ─────────────────────────────

public sealed record UpdateSavingsGoalCommand(
    Guid WorkspaceId,
    Guid SavingsGoalId,
    string Name,
    decimal TargetAmount,
    DateOnly? TargetDate,
    Guid? LinkedWalletId,
    decimal? CurrentAmount = null) : IRequest<SavingsGoalDto>;

public sealed class UpdateSavingsGoalCommandValidator : AbstractValidator<UpdateSavingsGoalCommand>
{
    public UpdateSavingsGoalCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.SavingsGoalId).NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Birikim hedefi adı zorunludur.")
            .MaximumLength(150).WithMessage("Birikim hedefi adı en fazla 150 karakter olabilir.");

        RuleFor(x => x.TargetAmount)
            .GreaterThan(0m).WithMessage("Hedef tutar sıfırdan büyük olmalıdır.")
            .PrecisionScale(19, 4, ignoreTrailingZeros: true)
            .WithMessage("Hedef tutar NUMERIC(19,4) hassasiyetini aşamaz.");

        RuleFor(x => x.CurrentAmount!.Value)
            .GreaterThanOrEqualTo(0m).WithMessage("Mevcut birikim tutarı negatif olamaz.")
            .PrecisionScale(19, 4, ignoreTrailingZeros: true)
            .When(x => x.CurrentAmount.HasValue);
    }
}

public sealed class UpdateSavingsGoalCommandHandler
    : IRequestHandler<UpdateSavingsGoalCommand, SavingsGoalDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateSavingsGoalCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<SavingsGoalDto> Handle(
        UpdateSavingsGoalCommand request,
        CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(request.WorkspaceId, WorkspaceRole.Admin, cancellationToken);

        var goal = await _dbContext.SavingsGoals
            .FirstOrDefaultAsync(
                sg => sg.Id == request.SavingsGoalId && sg.WorkspaceId == request.WorkspaceId && !sg.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException("SAVINGS_GOAL_NOT_FOUND", "Birikim hedefi bu çalışma alanında bulunamadı.");

        Wallet? linkedWallet = null;
        if (request.LinkedWalletId.HasValue)
        {
            linkedWallet = await _dbContext.Wallets
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    w => w.Id == request.LinkedWalletId.Value && w.WorkspaceId == request.WorkspaceId && !w.IsDeleted,
                    cancellationToken)
                ?? throw new NotFoundException("WALLET_NOT_FOUND", "Bağlanacak cüzdan bu çalışma alanında bulunamadı.");
        }

        var before = new
        {
            goal.Name,
            goal.TargetAmount,
            goal.CurrentAmount,
            goal.LinkedWalletId,
            goal.TargetDate
        };

        goal.Update(
            request.Name,
            request.TargetAmount,
            request.TargetDate,
            request.LinkedWalletId,
            request.CurrentAmount);

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId,
                access.UserId,
                "SavingsGoalUpdated",
                nameof(SavingsGoal),
                goal.Id,
                oldValuesJson: AuditJson.Serialize(before),
                newValuesJson: AuditJson.Serialize(new
                {
                    goal.Name,
                    goal.TargetAmount,
                    goal.CurrentAmount,
                    goal.LinkedWalletId,
                    goal.TargetDate
                }),
                utcNow: _dateTimeProvider.UtcNow));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var effectiveCurrent = linkedWallet?.CurrentBalance ?? goal.CurrentAmount;

        return SavingsGoalMapper.ToDto(
            goal.Id,
            goal.WorkspaceId,
            goal.LinkedWalletId,
            linkedWallet?.Name,
            goal.Name,
            goal.TargetAmount,
            effectiveCurrent,
            goal.CurrencyCode,
            goal.TargetDate);
    }
}

// ───────────────────────────── Delete ─────────────────────────────

public sealed record DeleteSavingsGoalCommand(Guid WorkspaceId, Guid SavingsGoalId) : IRequest;

public sealed class DeleteSavingsGoalCommandHandler : IRequestHandler<DeleteSavingsGoalCommand>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly IDateTimeProvider _dateTimeProvider;

    public DeleteSavingsGoalCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task Handle(DeleteSavingsGoalCommand request, CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(request.WorkspaceId, WorkspaceRole.Admin, cancellationToken);

        var goal = await _dbContext.SavingsGoals
            .FirstOrDefaultAsync(
                sg => sg.Id == request.SavingsGoalId && sg.WorkspaceId == request.WorkspaceId && !sg.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException("SAVINGS_GOAL_NOT_FOUND", "Birikim hedefi bu çalışma alanında bulunamadı.");

        goal.SoftDelete();

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId,
                access.UserId,
                "SavingsGoalDeleted",
                nameof(SavingsGoal),
                goal.Id,
                oldValuesJson: AuditJson.Serialize(new { goal.Name, goal.TargetAmount, goal.CurrentAmount }),
                newValuesJson: null,
                utcNow: _dateTimeProvider.UtcNow));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
