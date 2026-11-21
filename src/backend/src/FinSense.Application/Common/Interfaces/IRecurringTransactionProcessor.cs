namespace FinSense.Application.Common.Interfaces;

public interface IRecurringTransactionProcessor
{
    Task<int> ProcessDueTransactionsAsync(
        Guid? workspaceId = null,
        CancellationToken cancellationToken = default);
}
