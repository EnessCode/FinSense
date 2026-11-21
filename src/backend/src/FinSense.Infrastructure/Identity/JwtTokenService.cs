using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FinSense.Application.Common.Interfaces;
using FinSense.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FinSense.Infrastructure.Identity;

public class JwtTokenService : ITokenService
{
    private readonly JwtOptions _jwtOptions;
    private readonly IDateTimeProvider _dateTimeProvider;

    public JwtTokenService(
        IOptions<JwtOptions> jwtOptions,
        IDateTimeProvider dateTimeProvider)
    {
        _jwtOptions = jwtOptions.Value;
        _dateTimeProvider = dateTimeProvider;
    }

    public AccessTokenResult GenerateAccessToken(User user)
    {
        if (string.IsNullOrWhiteSpace(_jwtOptions.SigningKey) ||
            _jwtOptions.SigningKey == "SET_VIA_USER_SECRETS_OR_ENV_VAR" ||
            Encoding.UTF8.GetByteCount(_jwtOptions.SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey en az 32 byte uzunluğunda güvenli bir değer olarak User Secrets veya ortam değişkenlerinde yapılandırılmalıdır.");
        }

        var now = _dateTimeProvider.UtcNow;
        var expiresAtUtc = now.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("display_name", user.DisplayName),
            new("locale", user.PreferredLocale),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SigningKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);

        return new AccessTokenResult(accessToken, expiresAtUtc);
    }

    public RefreshTokenGenerationResult GenerateRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        var rawToken = Convert.ToBase64String(randomBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var tokenHash = ComputeTokenHash(rawToken);
        var expiresAtUtc = _dateTimeProvider.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays);

        return new RefreshTokenGenerationResult(rawToken, tokenHash, expiresAtUtc);
    }

    public string ComputeTokenHash(string rawRefreshToken)
    {
        var bytes = Encoding.UTF8.GetBytes(rawRefreshToken.Trim());
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
