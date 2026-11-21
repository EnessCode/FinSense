using FinSense.Application.Common.Auditing;
using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Wallets.Commands.DeleteWallet;

public sealed record DeleteWalletCommand(Guid WorkspaceId, Guid WalletId) : IRequest;

public sealed class DeleteWalletCommandValidator : AbstractValidator<DeleteWalletCommand>
{
    public DeleteWalletCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.WalletId).NotEmpty();
    }
}

public sealed class DeleteWalletCommandHandler : IRequestHandler<DeleteWalletCommand>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly IDateTimeProvider _dateTimeProvider;

    public DeleteWalletCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task Handle(DeleteWalletCommand request, CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Admin,
            cancellationToken);

        var wallet = await _dbContext.Wallets
            .FirstOrDefaultAsync(
                w => w.Id == request.WalletId && w.WorkspaceId == request.WorkspaceId && !w.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException("WALLET_NOT_FOUND", "Cüzdan bu çalışma alanında bulunamadı.");

        // Soft-delete preserves foreign-key integrity for historical transactions and analytics.
        wallet.SoftDelete();

        // Deactivate any active recurring instructions targeting the deleted wallet.
        var activeRecurringRules = await _dbContext.RecurringTransactions
            .Where(r => r.WorkspaceId == request.WorkspaceId && r.WalletId == wallet.Id && r.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var rule in activeRecurringRules)
        {
            rule.Deactivate();
        }

        // Unlink any savings goals pointing to this wallet while preserving their saved amount.
        var linkedGoals = await _dbContext.SavingsGoals
            .Where(g => g.WorkspaceId == request.WorkspaceId && g.LinkedWalletId == wallet.Id && !g.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var goal in linkedGoals)
        {
            var preservedAmount = Math.Max(0m, wallet.CurrentBalance);
            goal.Update(
                goal.Name,
                goal.TargetAmount,
                goal.TargetDate,
                linkedWalletId: null,
                manualCurrentAmount: preservedAmount);
        }

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId,
                access.UserId,
                "WalletDeleted",
                nameof(Wallet),
                wallet.Id,
                oldValuesJson: AuditJson.Serialize(new
                {
                    wallet.Name,
                    wallet.Type,
                    wallet.CurrencyCode,
                    wallet.InitialBalance,
                    wallet.CurrentBalance,
                    DeactivatedRecurringCount = activeRecurringRules.Count,
                    UnlinkedSavingsGoalCount = linkedGoals.Count
                }),
                newValuesJson: null,
                utcNow: _dateTimeProvider.UtcNow));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
