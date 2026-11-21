using FinSense.Application.Common.Interfaces;
using FinSense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.RecurringTransactions.Services;

public sealed class RecurringTransactionProcessor : IRecurringTransactionProcessor
{
    private const int MaxCatchUpCyclesPerPass = 120;

    private readonly IAppDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RecurringTransactionProcessor(
        IAppDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<int> ProcessDueTransactionsAsync(
        Guid? workspaceId = null,
        CancellationToken cancellationToken = default)
    {
        var now = _dateTimeProvider.UtcNow;

        var query = _dbContext.RecurringTransactions
            .Where(r => r.IsActive && r.NextExecutionDateUtc <= now);

        if (workspaceId.HasValue)
        {
            query = query.Where(r => r.WorkspaceId == workspaceId.Value);
        }

        var dueRules = await query
            .OrderBy(r => r.NextExecutionDateUtc)
            .ToListAsync(cancellationToken);

        if (dueRules.Count == 0)
        {
            return 0;
        }

        var walletIds = dueRules.Select(r => r.WalletId).Distinct().ToList();
        var workspaceIds = dueRules.Select(r => r.WorkspaceId).Distinct().ToList();

        var walletsById = await _dbContext.Wallets
            .Where(w => !w.IsDeleted && walletIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, cancellationToken);

        var existingKeysList = await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => workspaceIds.Contains(t.WorkspaceId) &&
                        t.IdempotencyKey != null &&
                        t.IdempotencyKey.StartsWith("recurring-"))
            .Select(t => new { t.WorkspaceId, IdempotencyKey = t.IdempotencyKey! })
            .ToListAsync(cancellationToken);

        var existingKeys = existingKeysList
            .Select(x => (x.WorkspaceId, x.IdempotencyKey))
            .ToHashSet();

        var createdTransactionsCount = 0;
        var hasChanges = false;

        foreach (var rule in dueRules)
        {
            if (!walletsById.TryGetValue(rule.WalletId, out var wallet))
            {
                rule.Deactivate();
                hasChanges = true;
                continue;
            }

            if (wallet.IsArchived)
            {
                continue;
            }

            var cycles = 0;
            while (rule.IsActive && rule.NextExecutionDateUtc <= now && cycles < MaxCatchUpCyclesPerPass)
            {
                cycles++;
                var cycleDate = rule.NextExecutionDateUtc;
                var idempotencyKey = $"recurring-{rule.Id}-{cycleDate:yyyyMMdd}";

                if (existingKeys.Contains((rule.WorkspaceId, idempotencyKey)))
                {
                    // Cycle was already recorded; advance pointer without double-charging
                    rule.RecordScheduledExecution(now);
                    hasChanges = true;
                    continue;
                }

                var occurrenceNumber = rule.CompletedOccurrences + 1;
                var description = rule.FormatCycleDescription(occurrenceNumber);

                var transaction = Transaction.Create(
                    workspaceId: rule.WorkspaceId,
                    walletId: rule.WalletId,
                    type: rule.Type,
                    amount: rule.Amount,
                    transactionDate: DateOnly.FromDateTime(cycleDate),
                    categoryId: rule.CategoryId,
                    counterpartyWalletId: null,
                    receiptId: null,
                    currencyCode: rule.Currency,
                    merchantName: rule.Title,
                    notes: description,
                    createdByUserId: rule.CreatedByUserId,
                    paidByUserId: rule.CreatedByUserId,
                    idempotencyKey: idempotencyKey,
                    utcNow: now);

                transaction.ApplyToWallets(wallet);

                _dbContext.Transactions.Add(transaction);
                existingKeys.Add((rule.WorkspaceId, idempotencyKey));

                rule.RecordScheduledExecution(now);
                createdTransactionsCount++;
                hasChanges = true;
            }
        }

        if (hasChanges)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return createdTransactionsCount;
    }
}
