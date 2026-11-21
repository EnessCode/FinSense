using System.Collections.Concurrent;
using FinSense.Application.Common.Interfaces;
using FinSense.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace FinSense.Infrastructure.Services;

public sealed class InMemoryAiQuotaService : IAiQuotaService
{
    private sealed class CounterBox
    {
        public int Count;
    }

    private readonly ConcurrentDictionary<(Guid UserId, DateOnly UtcDate, AiQuotaType QuotaType), CounterBox> _counters = new();
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IOptions<AiOptions> _aiOptions;

    public InMemoryAiQuotaService(
        IDateTimeProvider dateTimeProvider,
        IOptions<AiOptions> aiOptions)
    {
        _dateTimeProvider = dateTimeProvider;
        _aiOptions = aiOptions;
    }

    public int ConsumeDailyQuota(Guid userId, AiQuotaType quotaType)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("INVALID_USER_ID", "Geçerli bir kullanıcı kimliği gereklidir.");
        }

        var todayUtc = DateOnly.FromDateTime(_dateTimeProvider.UtcNow);
        var options = _aiOptions.Value;

        var dailyLimit = quotaType switch
        {
            AiQuotaType.Chat => options.DailyChatQuotaPerUser,
            AiQuotaType.ReceiptScan => options.DailyReceiptQuotaPerUser,
            _ => throw new ArgumentOutOfRangeException(nameof(quotaType), quotaType, null)
        };

        if (dailyLimit <= 0)
        {
            throw new DomainException(
                "AI_DAILY_QUOTA_EXCEEDED",
                quotaType == AiQuotaType.Chat
                    ? "Günlük yapay zeka sohbet hakkınız bulunmuyor."
                    : "Günlük fiş tarama hakkınız bulunmuyor.");
        }

        PruneStaleDaysIfNeeded(todayUtc);

        var key = (userId, todayUtc, quotaType);
        var box = _counters.GetOrAdd(key, _ => new CounterBox());

        lock (box)
        {
            if (box.Count >= dailyLimit)
            {
                var errorMessage = quotaType == AiQuotaType.Chat
                    ? $"Günlük yapay zeka sohbet limitinize ({dailyLimit}) ulaştınız. Yarın tekrar deneyebilirsiniz."
                    : $"Günlük fiş tarama limitinize ({dailyLimit}) ulaştınız. Yarın tekrar deneyebilirsiniz.";

                throw new DomainException("AI_DAILY_QUOTA_EXCEEDED", errorMessage);
            }

            box.Count++;
            return dailyLimit - box.Count;
        }
    }

    public int GetRemainingDailyQuota(Guid userId, AiQuotaType quotaType)
    {
        if (userId == Guid.Empty)
        {
            return 0;
        }

        var todayUtc = DateOnly.FromDateTime(_dateTimeProvider.UtcNow);
        var options = _aiOptions.Value;

        var dailyLimit = quotaType switch
        {
            AiQuotaType.Chat => options.DailyChatQuotaPerUser,
            AiQuotaType.ReceiptScan => options.DailyReceiptQuotaPerUser,
            _ => throw new ArgumentOutOfRangeException(nameof(quotaType), quotaType, null)
        };

        if (dailyLimit <= 0)
        {
            return 0;
        }

        var key = (userId, todayUtc, quotaType);
        if (!_counters.TryGetValue(key, out var box))
        {
            return dailyLimit;
        }

        lock (box)
        {
            return Math.Max(0, dailyLimit - box.Count);
        }
    }

    private void PruneStaleDaysIfNeeded(DateOnly todayUtc)
    {
        if (_counters.Count <= 1024)
        {
            return;
        }

        foreach (var existingKey in _counters.Keys)
        {
            if (existingKey.UtcDate < todayUtc)
            {
                _counters.TryRemove(existingKey, out _);
            }
        }
    }
}
