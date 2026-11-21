namespace FinSense.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    string Locale { get; }
    bool IsAuthenticated { get; }
    Guid GetRequiredUserId();
}
