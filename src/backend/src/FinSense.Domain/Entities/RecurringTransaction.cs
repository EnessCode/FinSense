using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;

namespace FinSense.Domain.Entities;

public class RecurringTransaction
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid WalletId { get; private set; }
    public Guid CategoryId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public TransactionType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public string Title { get; private set; } = string.Empty;
    public int DayOfMonth { get; private set; }
    public int? TotalOccurrences { get; private set; }
    public int CompletedOccurrences { get; private set; }
    public DateTime StartDateUtc { get; private set; }
    public DateTime NextExecutionDateUtc { get; private set; }
    public DateTime? LastExecutedAtUtc { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public Workspace Workspace { get; private set; } = null!;
    public Wallet Wallet { get; private set; } = null!;
    public Category Category { get; private set; } = null!;
    public User CreatedByUser { get; private set; } = null!;

    private RecurringTransaction() { }

    public static RecurringTransaction Create(
        Guid workspaceId,
        Guid walletId,
        Guid categoryId,
        Guid createdByUserId,
        TransactionType type,
        decimal amount,
        string title,
        int dayOfMonth,
        int? totalOccurrences = null,
        string currency = "TRY",
        bool executeFirstImmediately = false,
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

        if (categoryId == Guid.Empty)
        {
            throw new DomainException("INVALID_CATEGORY_ID", "Geçerli bir kategori kimliği gereklidir.");
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new DomainException("INVALID_USER_ID", "Geçerli bir kullanıcı kimliği gereklidir.");
        }

        if (type is not (TransactionType.Income or TransactionType.Expense))
        {
            throw new DomainException(
                "INVALID_RECURRING_TRANSACTION_TYPE",
                "Düzenli işlemler yalnızca Gelir (Income) veya Gider (Expense) türünde olabilir.");
        }

        if (amount <= 0m)
        {
            throw new DomainException("INVALID_TRANSACTION_AMOUNT", "İşlem tutarı sıfırdan büyük olmalıdır.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("RECURRING_TITLE_REQUIRED", "Düzenli işlem başlığı zorunludur.");
        }

        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length > 150)
        {
            throw new DomainException("RECURRING_TITLE_TOO_LONG", "Düzenli işlem başlığı en fazla 150 karakter olabilir.");
        }

        if (dayOfMonth is < 1 or > 31)
        {
            throw new DomainException("INVALID_DAY_OF_MONTH", "Ayın günü 1 ile 31 arasında olmalıdır.");
        }

        if (totalOccurrences.HasValue && totalOccurrences.Value is < 1 or > 120)
        {
            throw new DomainException(
                "INVALID_TOTAL_OCCURRENCES",
                "Toplam tekrar sayısı boş bırakılmalı veya 1 ile 120 arasında olmalıdır.");
        }

        var now = utcNow ?? DateTime.UtcNow;
        var normalizedCurrency = Workspace.NormalizeCurrencyCode(currency);
        var roundedAmount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

        var thisMonthBillingDate = ComputeBillingDateUtc(now.Year, now.Month, dayOfMonth);
        var nextExecution = (executeFirstImmediately || now.Day == dayOfMonth || thisMonthBillingDate.Date < now.Date)
            ? ComputeNextMonthBillingDateUtc(now.Year, now.Month, dayOfMonth)
            : thisMonthBillingDate;

        return new RecurringTransaction
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            WalletId = walletId,
            CategoryId = categoryId,
            CreatedByUserId = createdByUserId,
            Type = type,
            Amount = roundedAmount,
            Currency = normalizedCurrency,
            Title = trimmedTitle,
            DayOfMonth = dayOfMonth,
            TotalOccurrences = totalOccurrences,
            CompletedOccurrences = 0,
            StartDateUtc = now,
            NextExecutionDateUtc = nextExecution,
            LastExecutedAtUtc = null,
            IsActive = true,
            CreatedAtUtc = now
        };
    }

    public static DateTime ComputeBillingDateUtc(int year, int month, int dayOfMonth)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var clampedDay = Math.Min(Math.Max(dayOfMonth, 1), daysInMonth);
        return new DateTime(year, month, clampedDay, 0, 0, 0, DateTimeKind.Utc);
    }

    public static DateTime ComputeNextMonthBillingDateUtc(int year, int month, int dayOfMonth)
    {
        var firstOfNextMonth = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        return ComputeBillingDateUtc(firstOfNextMonth.Year, firstOfNextMonth.Month, dayOfMonth);
    }

    public static DateTime ComputeUpcomingBillingDateUtc(DateTime utcNow, int dayOfMonth)
    {
        var thisMonth = ComputeBillingDateUtc(utcNow.Year, utcNow.Month, dayOfMonth);
        return thisMonth.Date >= utcNow.Date
            ? thisMonth
            : ComputeNextMonthBillingDateUtc(utcNow.Year, utcNow.Month, dayOfMonth);
    }

    public string FormatCycleDescription(int occurrenceNumber)
    {
        return TotalOccurrences.HasValue
            ? $"{Title} ({occurrenceNumber}/{TotalOccurrences.Value})"
            : $"{Title} ({occurrenceNumber}. Dönem)";
    }

    public void RecordInitialImmediateExecution(DateTime executedAtUtc)
    {
        CompletedOccurrences = 1;
        LastExecutedAtUtc = executedAtUtc;
        NextExecutionDateUtc = ComputeNextMonthBillingDateUtc(executedAtUtc.Year, executedAtUtc.Month, DayOfMonth);

        if (TotalOccurrences.HasValue && CompletedOccurrences >= TotalOccurrences.Value)
        {
            IsActive = false;
        }
    }

    public void RecordScheduledExecution(DateTime executedAtUtc)
    {
        CompletedOccurrences++;
        LastExecutedAtUtc = executedAtUtc;

        if (TotalOccurrences.HasValue && CompletedOccurrences >= TotalOccurrences.Value)
        {
            IsActive = false;
            return;
        }

        NextExecutionDateUtc = ComputeNextMonthBillingDateUtc(
            NextExecutionDateUtc.Year,
            NextExecutionDateUtc.Month,
            DayOfMonth);
    }

    public void Toggle(DateTime utcNow)
    {
        if (!IsActive && TotalOccurrences.HasValue && CompletedOccurrences >= TotalOccurrences.Value)
        {
            throw new DomainException(
                "RECURRING_TRANSACTION_COMPLETED",
                "Tüm dönemleri tamamlanmış düzenli işlem yeniden aktifleştirilemez.");
        }

        IsActive = !IsActive;

        if (IsActive && NextExecutionDateUtc.Date < utcNow.Date)
        {
            NextExecutionDateUtc = ComputeUpcomingBillingDateUtc(utcNow, DayOfMonth);
        }
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
