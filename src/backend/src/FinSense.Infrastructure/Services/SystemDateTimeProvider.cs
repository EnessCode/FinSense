using FinSense.Application.Common.Interfaces;

namespace FinSense.Infrastructure.Services;

public class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly TodayIstanbul
    {
        get
        {
            var istanbulNow = UtcNow.AddHours(3);
            return DateOnly.FromDateTime(istanbulNow);
        }
    }
}
