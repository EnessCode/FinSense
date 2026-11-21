namespace FinSense.Application.Features.Analytics.Contracts;

public sealed record CategoryBreakdownItemDto(
    Guid CategoryId,
    string CategoryName,
    string Icon,
    string ColorHex,
    decimal Amount,
    decimal Percentage,
    int TransactionCount);

public sealed record MemberBreakdownItemDto(
    Guid? UserId,
    string DisplayName,
    decimal IncomeAmount,
    decimal ExpenseAmount,
    int TransactionCount);

public sealed record AnalyticsSummaryDto(
    DateOnly DateFrom,
    DateOnly DateTo,
    string CurrencyCode,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal NetCashFlow,
    decimal SavingsRatePercent,
    IReadOnlyList<CategoryBreakdownItemDto> CategoryBreakdown,
    IReadOnlyList<MemberBreakdownItemDto> MemberBreakdown);
