using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;

namespace FinSense.Domain.Entities;

public class Wallet
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public WalletType Type { get; private set; }
    public string CurrencyCode { get; private set; } = "TRY";
    public decimal InitialBalance { get; private set; }
    public decimal CurrentBalance { get; private set; }
    public decimal? CreditLimit { get; private set; }
    public string? Icon { get; private set; }
    public string? ColorHex { get; private set; }
    public bool IsArchived { get; private set; }
    public bool IsDeleted { get; private set; }

    public Workspace Workspace { get; private set; } = null!;

    private Wallet() { }

    public static Wallet Create(
        Guid workspaceId,
        string name,
        WalletType type,
        decimal initialBalance = 0m,
        string currencyCode = "TRY",
        decimal? creditLimit = null,
        string? icon = null,
        string? colorHex = null)
    {
        if (workspaceId == Guid.Empty)
        {
            throw new DomainException("INVALID_WORKSPACE_ID", "Geçerli bir çalışma alanı kimliği gereklidir.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("WALLET_NAME_REQUIRED", "Cüzdan adı zorunludur.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new DomainException("INVALID_WALLET_TYPE", "Geçersiz cüzdan türü.");
        }

        if (creditLimit.HasValue && creditLimit.Value < 0m)
        {
            throw new DomainException("INVALID_CREDIT_LIMIT", "Kredi limiti negatif olamaz.");
        }

        var normalizedCurrency = Workspace.NormalizeCurrencyCode(currencyCode);
        var roundedInitial = decimal.Round(initialBalance, 4, MidpointRounding.AwayFromZero);

        return new Wallet
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name = name.Trim(),
            Type = type,
            CurrencyCode = normalizedCurrency,
            InitialBalance = roundedInitial,
            CurrentBalance = roundedInitial,
            CreditLimit = creditLimit.HasValue
                ? decimal.Round(creditLimit.Value, 4, MidpointRounding.AwayFromZero)
                : null,
            Icon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim(),
            ColorHex = string.IsNullOrWhiteSpace(colorHex) ? null : colorHex.Trim(),
            IsArchived = false,
            IsDeleted = false
        };
    }

    public void ApplyIncome(decimal amount)
    {
        EnsureCanRecordTransaction();
        EnsurePositiveAmount(amount);

        CurrentBalance = decimal.Round(CurrentBalance + amount, 4, MidpointRounding.AwayFromZero);
    }

    public void ApplyExpense(decimal amount)
    {
        EnsureCanRecordTransaction();
        EnsurePositiveAmount(amount);

        CurrentBalance = decimal.Round(CurrentBalance - amount, 4, MidpointRounding.AwayFromZero);
    }

    public void RevertIncome(decimal amount)
    {
        EnsurePositiveAmount(amount);
        CurrentBalance = decimal.Round(CurrentBalance - amount, 4, MidpointRounding.AwayFromZero);
    }

    public void RevertExpense(decimal amount)
    {
        EnsurePositiveAmount(amount);
        CurrentBalance = decimal.Round(CurrentBalance + amount, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Updates the wallet's initial balance and shifts <see cref="CurrentBalance"/> by the exact delta,
    /// preserving all historical transaction impacts without arbitrary balance overwrites.
    /// </summary>
    public decimal AdjustInitialBalance(decimal newInitialBalance)
    {
        EnsureNotDeleted();

        var roundedNewInitial = decimal.Round(newInitialBalance, 4, MidpointRounding.AwayFromZero);
        var delta = roundedNewInitial - InitialBalance;
        InitialBalance = roundedNewInitial;
        CurrentBalance = decimal.Round(CurrentBalance + delta, 4, MidpointRounding.AwayFromZero);
        return delta;
    }

    public void UpdateMetadata(
        string name,
        decimal? creditLimit,
        string? icon,
        string? colorHex,
        bool isArchived)
    {
        EnsureNotDeleted();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("WALLET_NAME_REQUIRED", "Cüzdan adı zorunludur.");
        }

        if (creditLimit.HasValue && creditLimit.Value < 0m)
        {
            throw new DomainException("INVALID_CREDIT_LIMIT", "Kredi limiti negatif olamaz.");
        }

        Name = name.Trim();
        CreditLimit = creditLimit.HasValue
            ? decimal.Round(creditLimit.Value, 4, MidpointRounding.AwayFromZero)
            : null;
        Icon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();
        ColorHex = string.IsNullOrWhiteSpace(colorHex) ? null : colorHex.Trim();
        IsArchived = isArchived;
    }

    public void Archive()
    {
        EnsureNotDeleted();
        IsArchived = true;
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        IsArchived = true;
    }

    private void EnsureCanRecordTransaction()
    {
        EnsureNotDeleted();

        if (IsArchived)
        {
            throw new DomainException("WALLET_ARCHIVED", "Arşivlenmiş cüzdana yeni işlem eklenemez.");
        }
    }

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
        {
            throw new DomainException("WALLET_DELETED", "Silinmiş cüzdan üzerinde işlem yapılamaz.");
        }
    }

    private static void EnsurePositiveAmount(decimal amount)
    {
        if (amount <= 0m)
        {
            throw new DomainException("INVALID_TRANSACTION_AMOUNT", "İşlem tutarı sıfırdan büyük olmalıdır.");
        }
    }
}
