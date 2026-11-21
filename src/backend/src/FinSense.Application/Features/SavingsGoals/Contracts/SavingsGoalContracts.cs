namespace FinSense.Application.Features.SavingsGoals.Contracts;

public sealed record SavingsGoalDto(
    Guid Id,
    Guid WorkspaceId,
    Guid? LinkedWalletId,
    string? LinkedWalletName,
    string Name,
    decimal TargetAmount,
    decimal CurrentAmount,
    decimal RemainingAmount,
    decimal CompletionPercent,
    string CurrencyCode,
    DateOnly? TargetDate);

internal static class SavingsGoalMapper
{
    public static SavingsGoalDto ToDto(
        Guid id,
        Guid workspaceId,
        Guid? linkedWalletId,
        string? linkedWalletName,
        string name,
        decimal targetAmount,
        decimal effectiveCurrentAmount,
        string currencyCode,
        DateOnly? targetDate)
    {
        var remaining = Math.Max(
            0m,
            decimal.Round(targetAmount - effectiveCurrentAmount, 4, MidpointRounding.AwayFromZero));

        var completionPercent = targetAmount > 0m && effectiveCurrentAmount > 0m
            ? decimal.Round((effectiveCurrentAmount / targetAmount) * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        return new SavingsGoalDto(
            id,
            workspaceId,
            linkedWalletId,
            linkedWalletName,
            name,
            targetAmount,
            effectiveCurrentAmount,
            remaining,
            completionPercent,
            currencyCode,
            targetDate);
    }
}
