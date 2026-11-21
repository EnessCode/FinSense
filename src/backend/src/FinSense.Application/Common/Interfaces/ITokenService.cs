using FinSense.Domain.Entities;

namespace FinSense.Application.Common.Interfaces;

public record AccessTokenResult(string AccessToken, DateTime ExpiresAtUtc);

public record RefreshTokenGenerationResult(
    string RawToken,
    string TokenHash,
    DateTime ExpiresAtUtc);

public interface ITokenService
{
    AccessTokenResult GenerateAccessToken(User user);
    RefreshTokenGenerationResult GenerateRefreshToken();
    string ComputeTokenHash(string rawRefreshToken);
}
