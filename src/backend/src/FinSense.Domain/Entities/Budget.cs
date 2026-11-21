using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;

namespace FinSense.Domain.Entities;

public class Budget
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public short Year { get; private set; }
    public short Month { get; private set; }
    public decimal LimitAmount { get; private set; }
    public string CurrencyCode { get; private set; } = "TRY";
    public short AlertThresholdPercent { get; private set; } = 80;
    public bool IsDeleted { get; private set; }

    public Workspace Workspace { get; private set; } = null!;
    public Category? Category { get; private set; }

    private Budget() { }

    public static Budget Create(
        Guid workspaceId,
        short year,
        short month,
        decimal limitAmount,
        Guid? categoryId = null,
        string currencyCode = "TRY",
        short alertThresholdPercent = 80)
    {
        if (workspaceId == Guid.Empty)
        {
            throw new DomainException("INVALID_WORKSPACE_ID", "Geçerli bir çalışma alanı kimliği gereklidir.");
        }

        if (year < 2000 || year > 2100)
        {
            throw new DomainException("INVALID_BUDGET_YEAR", "Geçerli bir bütçe yılı belirtilmelidir.");
        }

        if (month < 1 || month > 12)
        {
            throw new DomainException("INVALID_BUDGET_MONTH", "Bütçe ayı 1 ile 12 arasında olmalıdır.");
        }

        if (limitAmount <= 0m)
        {
            throw new DomainException("INVALID_BUDGET_LIMIT", "Bütçe limiti sıfırdan büyük olmalıdır.");
        }

        if (alertThresholdPercent is < 1 or > 100)
        {
            throw new DomainException("INVALID_ALERT_THRESHOLD", "Uyarı eşiği yüzdesi 1 ile 100 arasında olmalıdır.");
        }

        if (categoryId.HasValue && categoryId.Value == Guid.Empty)
        {
            throw new DomainException("INVALID_CATEGORY_ID", "Geçersiz kategori kimliği.");
        }

        return new Budget
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            CategoryId = categoryId,
            Year = year,
            Month = month,
            LimitAmount = decimal.Round(limitAmount, 4, MidpointRounding.AwayFromZero),
            CurrencyCode = Workspace.NormalizeCurrencyCode(currencyCode),
            AlertThresholdPercent = alertThresholdPercent,
            IsDeleted = false
        };
    }

    public decimal CalculateUtilizationPercent(decimal spentAmount)
    {
        if (spentAmount <= 0m)
        {
            return 0m;
        }

        return decimal.Round((spentAmount / LimitAmount) * 100m, 2, MidpointRounding.AwayFromZero);
    }

    public BudgetStatus EvaluateStatus(decimal spentAmount)
    {
        var utilization = CalculateUtilizationPercent(spentAmount);

        if (spentAmount >= LimitAmount || utilization >= 100m)
        {
            return BudgetStatus.Exceeded;
        }

        if (utilization >= AlertThresholdPercent)
        {
            return BudgetStatus.Warning;
        }

        return BudgetStatus.Normal;
    }

    public void UpdateLimit(decimal newLimitAmount, short alertThresholdPercent = 80)
    {
        if (IsDeleted)
        {
            throw new DomainException("BUDGET_DELETED", "Silinmiş bütçe güncellenemez.");
        }

        if (newLimitAmount <= 0m)
        {
            throw new DomainException("INVALID_BUDGET_LIMIT", "Bütçe limiti sıfırdan büyük olmalıdır.");
        }

        if (alertThresholdPercent is < 1 or > 100)
        {
            throw new DomainException("INVALID_ALERT_THRESHOLD", "Uyarı eşiği yüzdesi 1 ile 100 arasında olmalıdır.");
        }

        LimitAmount = decimal.Round(newLimitAmount, 4, MidpointRounding.AwayFromZero);
        AlertThresholdPercent = alertThresholdPercent;
    }

    public void SoftDelete()
    {
        IsDeleted = true;
    }
}
