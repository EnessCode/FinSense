using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;

namespace FinSense.Domain.Entities;

public class Transaction
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid WalletId { get; private set; }
    public Guid? CounterpartyWalletId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public Guid? ReceiptId { get; private set; }
    public TransactionType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; } = "TRY";
    public decimal OriginalAmount { get; private set; }
    public string OriginalCurrencyCode { get; private set; } = "TRY";
    public decimal ExchangeRate { get; private set; } = 1.00000000m;
    public DateOnly TransactionDate { get; private set; }
    public string? MerchantName { get; private set; }
    public string? Notes { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public Guid? PaidByUserId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public Workspace Workspace { get; private set; } = null!;
    public Wallet Wallet { get; private set; } = null!;
    public Wallet? CounterpartyWallet { get; private set; }
    public Category? Category { get; private set; }
    public Receipt? Receipt { get; private set; }
    public User? CreatedByUser { get; private set; }
    public User? PaidByUser { get; private set; }

    private Transaction() { }

    public static Transaction Create(
        Guid workspaceId,
        Guid walletId,
        TransactionType type,
        decimal amount,
        DateOnly transactionDate,
        Guid? categoryId = null,
        Guid? counterpartyWalletId = null,
        Guid? receiptId = null,
        string currencyCode = "TRY",
        decimal? originalAmount = null,
        string? originalCurrencyCode = null,
        decimal exchangeRate = 1.00000000m,
        string? merchantName = null,
        string? notes = null,
        Guid? createdByUserId = null,
        Guid? paidByUserId = null,
        string? idempotencyKey = null,
        DateTime? utcNow = null)
    {
        if (workspaceId == Guid.Empty)
        {
            throw new DomainException("INVALID_WORKSPACE_ID", "Geçerli bir çalışma alanı kimliği gereklidir.");
        }

        if (walletId == Guid.Empty)
        {
            throw new DomainException("INVALID_WALLET_ID", "Geçerli bir cüzdan kimliği gereklidir.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new DomainException("INVALID_TRANSACTION_TYPE", "Geçersiz işlem türü.");
        }

        if (amount <= 0m)
        {
            throw new DomainException("INVALID_TRANSACTION_AMOUNT", "İşlem tutarı sıfırdan büyük olmalıdır.");
        }

        if (exchangeRate <= 0m)
        {
            throw new DomainException("INVALID_EXCHANGE_RATE", "Döviz kuru sıfırdan büyük olmalıdır.");
        }

        ValidateTypeInvariants(type, walletId, categoryId, counterpartyWalletId);

        var normalizedCurrency = Workspace.NormalizeCurrencyCode(currencyCode);
        var normalizedOriginalCurrency = string.IsNullOrWhiteSpace(originalCurrencyCode)
            ? normalizedCurrency
            : Workspace.NormalizeCurrencyCode(originalCurrencyCode);

        var roundedAmount = decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
        var roundedOriginalAmount = originalAmount.HasValue
            ? decimal.Round(originalAmount.Value, 4, MidpointRounding.AwayFromZero)
            : roundedAmount;

        if (roundedOriginalAmount <= 0m)
        {
            throw new DomainException("INVALID_TRANSACTION_AMOUNT", "Orijinal işlem tutarı sıfırdan büyük olmalıdır.");
        }

        return new Transaction
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            WalletId = walletId,
            CounterpartyWalletId = counterpartyWalletId,
            CategoryId = categoryId,
            ReceiptId = receiptId,
            Type = type,
            Amount = roundedAmount,
            CurrencyCode = normalizedCurrency,
            OriginalAmount = roundedOriginalAmount,
            OriginalCurrencyCode = normalizedOriginalCurrency,
            ExchangeRate = decimal.Round(exchangeRate, 8, MidpointRounding.AwayFromZero),
            TransactionDate = transactionDate,
            MerchantName = string.IsNullOrWhiteSpace(merchantName) ? null : merchantName.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedByUserId = createdByUserId,
            PaidByUserId = paidByUserId ?? createdByUserId,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            IsDeleted = false,
            DeletedAtUtc = null,
            CreatedAtUtc = utcNow ?? DateTime.UtcNow
        };
    }

    /// <summary>
    /// Applies this transaction's monetary impact to the target wallet(s) while enforcing
    /// workspace isolation and non-double-counting transfer rules.
    /// </summary>
    public void ApplyToWallets(Wallet primaryWallet, Wallet? counterpartyWallet = null)
    {
        if (primaryWallet.Id != WalletId || primaryWallet.WorkspaceId != WorkspaceId)
        {
            throw new DomainException("WALLET_MISMATCH", "Birincil cüzdan işlem çalışma alanı veya cüzdan kimliği ile eşleşmiyor.");
        }

        switch (Type)
        {
            case TransactionType.Income:
                primaryWallet.ApplyIncome(Amount);
                break;

            case TransactionType.Expense:
                primaryWallet.ApplyExpense(Amount);
                break;

            case TransactionType.Transfer:
                if (counterpartyWallet is null ||
                    counterpartyWallet.Id != CounterpartyWalletId ||
                    counterpartyWallet.WorkspaceId != WorkspaceId)
                {
                    throw new DomainException(
                        "INVALID_TRANSFER_COUNTERPARTY",
                        "Transfer işlemi için aynı çalışma alanında geçerli bir hedef cüzdan gereklidir.");
                }

                primaryWallet.ApplyExpense(Amount);
                counterpartyWallet.ApplyIncome(Amount);
                break;
        }
    }

    /// <summary>
    /// Updates the transaction's ledger fields while re-validating all type invariants.
    /// Callers must invoke <see cref="RevertFromWallets"/> BEFORE and <see cref="ApplyToWallets"/> AFTER
    /// this method so wallet balances are reconciled atomically within a single unit of work.
    /// </summary>
    public void Update(
        Guid walletId,
        TransactionType type,
        decimal amount,
        DateOnly transactionDate,
        Guid? categoryId,
        Guid? counterpartyWalletId,
        string? merchantName,
        string? notes,
        Guid? paidByUserId)
    {
        if (IsDeleted)
        {
            throw new DomainException("TRANSACTION_ALREADY_DELETED", "Silinmiş bir işlem güncellenemez.");
        }

        if (walletId == Guid.Empty)
        {
            throw new DomainException("INVALID_WALLET_ID", "Geçerli bir cüzdan kimliği gereklidir.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new DomainException("INVALID_TRANSACTION_TYPE", "Geçersiz işlem türü.");
        }

        if (amount <= 0m)
        {
            throw new DomainException("INVALID_TRANSACTION_AMOUNT", "İşlem tutarı sıfırdan büyük olmalıdır.");
        }

        ValidateTypeInvariants(type, walletId, categoryId, counterpartyWalletId);

        var roundedAmount = decimal.Round(amount, 4, MidpointRounding.AwayFromZero);

        WalletId = walletId;
        Type = type;
        Amount = roundedAmount;
        TransactionDate = transactionDate;
        CategoryId = categoryId;
        CounterpartyWalletId = counterpartyWalletId;
        MerchantName = string.IsNullOrWhiteSpace(merchantName) ? null : merchantName.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        PaidByUserId = paidByUserId ?? CreatedByUserId;

        // MVP single-currency: original amount mirrors amount while currencies are identical.
        if (string.Equals(OriginalCurrencyCode, CurrencyCode, StringComparison.Ordinal))
        {
            OriginalAmount = roundedAmount;
        }
    }

    /// <summary>
    /// Reverses this transaction's monetary impact on the wallet(s) it currently references.
    /// </summary>
    public void RevertFromWallets(Wallet primaryWallet, Wallet? counterpartyWallet = null)
    {
        if (IsDeleted)
        {
            throw new DomainException("TRANSACTION_ALREADY_DELETED", "İşlem zaten silinmiş.");
        }

        if (primaryWallet.Id != WalletId || primaryWallet.WorkspaceId != WorkspaceId)
        {
            throw new DomainException("WALLET_MISMATCH", "Birincil cüzdan işlem ile eşleşmiyor.");
        }

        switch (Type)
        {
            case TransactionType.Income:
                primaryWallet.RevertIncome(Amount);
                break;

            case TransactionType.Expense:
                primaryWallet.RevertExpense(Amount);
                break;

            case TransactionType.Transfer:
                if (counterpartyWallet is null ||
                    counterpartyWallet.Id != CounterpartyWalletId ||
                    counterpartyWallet.WorkspaceId != WorkspaceId)
                {
                    throw new DomainException(
                        "INVALID_TRANSFER_COUNTERPARTY",
                        "Transfer iptali için hedef cüzdan gereklidir.");
                }

                primaryWallet.RevertExpense(Amount);
                counterpartyWallet.RevertIncome(Amount);
                break;
        }
    }

    public void SoftDelete(DateTime utcNow, Wallet primaryWallet, Wallet? counterpartyWallet = null)
    {
        RevertFromWallets(primaryWallet, counterpartyWallet);

        IsDeleted = true;
        DeletedAtUtc = utcNow;
    }

    private static void ValidateTypeInvariants(
        TransactionType type,
        Guid walletId,
        Guid? categoryId,
        Guid? counterpartyWalletId)
    {
        if (type == TransactionType.Transfer)
        {
            if (!counterpartyWalletId.HasValue || counterpartyWalletId.Value == Guid.Empty)
            {
                throw new DomainException(
                    "TRANSFER_COUNTERPARTY_REQUIRED",
                    "Transfer işlemlerinde hedef cüzdan (CounterpartyWalletId) seçilmelidir.");
            }

            if (counterpartyWalletId.Value == walletId)
            {
                throw new DomainException(
                    "TRANSFER_SAME_WALLET_FORBIDDEN",
                    "Kaynak cüzdan ile hedef cüzdan aynı olamaz.");
            }

            if (categoryId.HasValue)
            {
                throw new DomainException(
                    "TRANSFER_CATEGORY_MUST_BE_NULL",
                    "Transfer işlemlerinde kategori seçilemez (gelir/gider toplamlarını şişirmemek için).");
            }
        }
        else
        {
            if (!categoryId.HasValue || categoryId.Value == Guid.Empty)
            {
                throw new DomainException(
                    "TRANSACTION_CATEGORY_REQUIRED",
                    "Gelir ve gider işlemlerinde kategori zorunludur.");
            }

            if (counterpartyWalletId.HasValue)
            {
                throw new DomainException(
                    "COUNTERPARTY_WALLET_NOT_ALLOWED",
                    "Yalnızca transfer işlemlerinde hedef cüzdan belirtilebilir.");
            }
        }
    }
}
