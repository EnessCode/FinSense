using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;

namespace FinSense.Api.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var rawId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);

            return Guid.TryParse(rawId, out var parsedId) ? parsedId : null;
        }
    }

    public string? Email =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Email)
        ?? _httpContextAccessor.HttpContext?.User?.FindFirstValue(JwtRegisteredClaimNames.Email);

    public string Locale
    {
        get
        {
            var acceptLanguage = _httpContextAccessor.HttpContext?.Request.Headers.AcceptLanguage.ToString();
            if (!string.IsNullOrWhiteSpace(acceptLanguage) &&
                acceptLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return "en-US";
            }

            var claimLocale = _httpContextAccessor.HttpContext?.User?.FindFirstValue("locale");
            if (!string.IsNullOrWhiteSpace(claimLocale) &&
                claimLocale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                return "en-US";
            }

            return "tr-TR";
        }
    }

    public bool IsAuthenticated => UserId.HasValue;

    public Guid GetRequiredUserId()
    {
        return UserId ?? throw new UnauthorizedException(
            "AUTHENTICATION_REQUIRED",
            "Bu işlemi gerçekleştirmek için oturum açmalısınız.");
    }
}
