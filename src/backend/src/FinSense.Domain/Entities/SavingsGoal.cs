using FinSense.Domain.Exceptions;

namespace FinSense.Domain.Entities;

public class SavingsGoal
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid? LinkedWalletId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal TargetAmount { get; private set; }
    public decimal CurrentAmount { get; private set; }
    public string CurrencyCode { get; private set; } = "TRY";
    public DateOnly? TargetDate { get; private set; }
    public bool IsDeleted { get; private set; }

    public Workspace Workspace { get; private set; } = null!;
    public Wallet? LinkedWallet { get; private set; }

    private SavingsGoal() { }

    public static SavingsGoal Create(
        Guid workspaceId,
        string name,
        decimal targetAmount,
        decimal currentAmount = 0m,
        Guid? linkedWalletId = null,
        DateOnly? targetDate = null,
        string currencyCode = "TRY")
    {
        if (workspaceId == Guid.Empty)
        {
            throw new DomainException("INVALID_WORKSPACE_ID", "Geçerli bir çalışma alanı kimliği gereklidir.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("SAVINGS_GOAL_NAME_REQUIRED", "Birikim hedefi adı zorunludur.");
        }

        if (targetAmount <= 0m)
        {
            throw new DomainException("INVALID_TARGET_AMOUNT", "Hedef tutar sıfırdan büyük olmalıdır.");
        }

        if (currentAmount < 0m)
        {
            throw new DomainException("INVALID_CURRENT_AMOUNT", "Mevcut birikim tutarı negatif olamaz.");
        }

        return new SavingsGoal
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            LinkedWalletId = linkedWalletId,
            Name = name.Trim(),
            TargetAmount = decimal.Round(targetAmount, 4, MidpointRounding.AwayFromZero),
            CurrentAmount = decimal.Round(currentAmount, 4, MidpointRounding.AwayFromZero),
            CurrencyCode = Workspace.NormalizeCurrencyCode(currencyCode),
            TargetDate = targetDate,
            IsDeleted = false
        };
    }

    public decimal GetEffectiveCurrentAmount(decimal? linkedWalletBalance = null)
    {
        if (LinkedWalletId.HasValue)
        {
            return linkedWalletBalance ?? LinkedWallet?.CurrentBalance ?? CurrentAmount;
        }

        return CurrentAmount;
    }

    public void Update(
        string name,
        decimal targetAmount,
        DateOnly? targetDate,
        Guid? linkedWalletId,
        decimal? manualCurrentAmount = null)
    {
        if (IsDeleted)
        {
            throw new DomainException("SAVINGS_GOAL_DELETED", "Silinmiş birikim hedefi güncellenemez.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("SAVINGS_GOAL_NAME_REQUIRED", "Birikim hedefi adı zorunludur.");
        }

        if (targetAmount <= 0m)
        {
            throw new DomainException("INVALID_TARGET_AMOUNT", "Hedef tutar sıfırdan büyük olmalıdır.");
        }

        if (linkedWalletId.HasValue && manualCurrentAmount.HasValue)
        {
            throw new DomainException(
                "LINKED_WALLET_GOAL_MANUAL_AMOUNT_FORBIDDEN",
                "Bir cüzdana bağlı birikim hedeflerinde mevcut tutar cüzdan bakiyesinden otomatik hesaplanır.");
        }

        Name = name.Trim();
        TargetAmount = decimal.Round(targetAmount, 4, MidpointRounding.AwayFromZero);
        TargetDate = targetDate;
        LinkedWalletId = linkedWalletId;

        if (!linkedWalletId.HasValue && manualCurrentAmount.HasValue)
        {
            if (manualCurrentAmount.Value < 0m)
            {
                throw new DomainException("INVALID_CURRENT_AMOUNT", "Mevcut birikim tutarı negatif olamaz.");
            }

            CurrentAmount = decimal.Round(manualCurrentAmount.Value, 4, MidpointRounding.AwayFromZero);
        }
    }

    public void SoftDelete()
    {
        IsDeleted = true;
    }
}
