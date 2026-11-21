using FinSense.Domain.Enums;

namespace FinSense.Application.Features.RecurringTransactions.Contracts;

public sealed record RecurringTransactionDto(
    Guid Id,
    Guid WalletId,
    string WalletName,
    Guid CategoryId,
    string CategoryName,
    TransactionType Type,
    decimal Amount,
    string Currency,
    string Title,
    int DayOfMonth,
    int? TotalOccurrences,
    int CompletedOccurrences,
    int? RemainingOccurrences,
    DateTime NextExecutionDateUtc,
    DateTime? LastExecutedAtUtc,
    bool IsActive);

public sealed record RecurringTransactionsOverviewDto(
    IReadOnlyList<RecurringTransactionDto> Items,
    decimal TotalMonthlyFixedIncome,
    decimal TotalMonthlyFixedExpense,
    decimal NetMonthlyFixedFlow);
