using FinSense.Domain.Enums;

namespace FinSense.Application.Features.Budgets.Contracts;

public sealed record BudgetDto(
    Guid Id,
    Guid WorkspaceId,
    Guid? CategoryId,
    string? CategoryName,
    string? CategoryIcon,
    string? CategoryColorHex,
    short Year,
    short Month,
    decimal LimitAmount,
    decimal SpentAmount,
    decimal RemainingAmount,
    decimal UtilizationPercent,
    short AlertThresholdPercent,
    BudgetStatus Status,
    string CurrencyCode);
