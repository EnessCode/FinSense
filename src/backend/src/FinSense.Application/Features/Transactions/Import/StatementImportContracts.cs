using FinSense.Domain.Enums;

namespace FinSense.Application.Features.Transactions.Import;

public sealed record ParsedStatementRow(
    DateOnly TransactionDate,
    string Description,
    decimal SignedAmount);

public sealed record ParsedStatementResult(
    string DetectedFormat,
    IReadOnlyList<ParsedStatementRow> Rows);

public sealed record ImportPreviewItemDto(
    int RowIndex,
    DateOnly TransactionDate,
    string Description,
    TransactionType Type,
    decimal Amount,
    Guid? CategoryId,
    string? CategoryName,
    string? CategoryIcon,
    string? CategoryColorHex,
    bool IsDuplicate);

public sealed record ImportPreviewResultDto(
    Guid WalletId,
    string WalletName,
    string CurrencyCode,
    string FileName,
    string DetectedFormat,
    int TotalRows,
    int DuplicateCount,
    decimal TotalIncome,
    decimal TotalExpense,
    IReadOnlyList<ImportPreviewItemDto> Items);

public sealed record ConfirmImportItemInput(
    DateOnly TransactionDate,
    string Description,
    TransactionType Type,
    decimal Amount,
    Guid CategoryId);

public sealed record ConfirmImportResultDto(
    Guid WalletId,
    int ImportedCount,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal UpdatedWalletBalance);
