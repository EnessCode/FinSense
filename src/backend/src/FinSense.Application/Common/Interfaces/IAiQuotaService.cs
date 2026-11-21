namespace FinSense.Application.Common.Interfaces;

public enum AiQuotaType
{
    Chat = 1,
    ReceiptScan = 2
}

public interface IAiQuotaService
{
    /// <summary>
    /// Atomically checks and consumes 1 unit of daily quota for the user and returns the remaining quota for today.
    /// Throws <see cref="FinSense.Domain.Exceptions.DomainException"/> if the daily limit has been reached.
    /// </summary>
    int ConsumeDailyQuota(Guid userId, AiQuotaType quotaType);

    /// <summary>
    /// Returns the remaining daily quota for the user today without consuming a unit.
    /// </summary>
    int GetRemainingDailyQuota(Guid userId, AiQuotaType quotaType);
}
