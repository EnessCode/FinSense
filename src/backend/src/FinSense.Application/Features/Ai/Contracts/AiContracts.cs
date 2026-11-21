namespace FinSense.Application.Features.Ai.Contracts;

public sealed record AiChatResponseDto(
    Guid ConversationId,
    bool IsFinanceRelated,
    string Intent,
    string Answer,
    string? HighlightedCategory,
    string? SuggestedAction,
    string ProviderUsed,
    int RemainingDailyQuota,
    DateTime CreatedAtUtc);

public sealed record AiChatMessageDto(
    Guid Id,
    string Role,
    string Content,
    string? Intent,
    bool? IsFinanceRelated,
    string? HighlightedCategory,
    string? SuggestedAction,
    string? ProviderUsed,
    DateTime CreatedAtUtc);

public sealed record AiChatHistoryResponseDto(
    Guid? ConversationId,
    string? Title,
    int RemainingDailyQuota,
    IReadOnlyList<AiChatMessageDto> Messages);

public sealed record ReceiptScanResultDto(
    bool IsValidReceipt,
    string? RejectionReason,
    string? MerchantName,
    string? TransactionDate,
    decimal? TotalAmount,
    decimal? TaxAmount,
    string Currency,
    string? SuggestedCategoryName,
    Guid? SuggestedCategoryId,
    double Confidence,
    string ProviderUsed,
    int RemainingDailyQuota);

internal sealed record AiMessageMetadata(
    bool IsFinanceRelated,
    string Intent,
    string? HighlightedCategory,
    string? SuggestedAction);
