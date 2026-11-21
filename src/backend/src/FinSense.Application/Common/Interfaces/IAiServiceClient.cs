namespace FinSense.Application.Common.Interfaces;

public sealed record AiCategorySpendItemDto(
    string CategoryName,
    decimal Amount,
    decimal? Percentage,
    int? TransactionCount);

public sealed record AiBudgetAlertItemDto(
    string CategoryName,
    decimal LimitAmount,
    decimal SpentAmount,
    decimal UtilizationPercent,
    string? Status);

public sealed record AiMemberSpendItemDto(
    string MemberName,
    decimal Amount,
    decimal? Percentage);

public sealed record AiRecurringObligationItemDto(
    string Title,
    string Type,
    decimal Amount,
    int DayOfMonth,
    int? TotalOccurrences,
    int CompletedOccurrences,
    DateTime NextExecutionDateUtc);

public sealed record AiInvestmentHoldingItemDto(
    string Symbol,
    string AssetName,
    string Category,
    decimal TotalQuantity,
    decimal TotalCost,
    decimal CurrentValue,
    decimal ProfitLossAmount,
    decimal ProfitLossPercent);

public sealed record AiInvestmentPortfolioDto(
    decimal TotalInvestedCost,
    decimal TotalCurrentValue,
    decimal TotalProfitLossAmount,
    decimal TotalProfitLossPercent,
    IReadOnlyList<AiInvestmentHoldingItemDto> TopHoldings);

public sealed record FinancialContextDto(
    decimal TotalBalance,
    decimal MonthlyIncome,
    decimal MonthlyExpense,
    decimal NetCashFlow,
    decimal SavingsRatePercent,
    IReadOnlyList<AiCategorySpendItemDto> TopCategories,
    IReadOnlyList<AiBudgetAlertItemDto> BudgetAlerts,
    IReadOnlyList<AiMemberSpendItemDto> MemberBreakdown,
    decimal MonthlyFixedIncome = 0m,
    decimal MonthlyFixedExpense = 0m,
    IReadOnlyList<AiRecurringObligationItemDto>? RecurringObligations = null,
    AiInvestmentPortfolioDto? InvestmentPortfolio = null);

public sealed record InternalAiChatRequest(
    string Question,
    string Locale,
    string WorkspaceType,
    FinancialContextDto FinancialContext);

public sealed record InternalAiChatResponse(
    bool IsFinanceRelated,
    string Intent,
    string Answer,
    string? HighlightedCategory,
    string? SuggestedAction,
    string ProviderUsed);

public sealed record InternalReceiptOcrRequest(
    string? ImageBase64,
    string? RawOcrText,
    string Locale,
    IReadOnlyList<string> AvailableCategories,
    string? MimeType);

public sealed record InternalReceiptOcrResponse(
    bool IsValidReceipt,
    string? RejectionReason,
    string? MerchantName,
    string? TransactionDate,
    decimal? TotalAmount,
    decimal? TaxAmount,
    string Currency,
    string? SuggestedCategory,
    double Confidence,
    string ProviderUsed);

public interface IAiServiceClient
{
    Task<InternalAiChatResponse> ChatAsync(
        InternalAiChatRequest request,
        CancellationToken cancellationToken = default);

    Task<InternalReceiptOcrResponse> ExtractReceiptAsync(
        InternalReceiptOcrRequest request,
        CancellationToken cancellationToken = default);
}
