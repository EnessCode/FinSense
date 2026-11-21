namespace FinSense.Application.Common.Interfaces;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    DateOnly TodayIstanbul { get; }
}
