namespace FinSense.Application.Features.Auth.Contracts;

public record AuthTokensDto(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);

public record AuthUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string PreferredLocale,
    Guid PersonalWorkspaceId);

public record AuthResponse(
    AuthUserDto User,
    AuthTokensDto Tokens);
