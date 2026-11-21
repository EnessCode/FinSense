using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Features.Auth.Contracts;
using FinSense.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Auth.Commands.Refresh;

public record RefreshTokenCommand(string RefreshToken) : IRequest<AuthTokensDto>;

public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Yenileme anahtarı (RefreshToken) zorunludur.");
    }
}

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthTokensDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly ITokenService _tokenService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RefreshTokenCommandHandler(
        IAppDbContext dbContext,
        ITokenService tokenService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _tokenService = tokenService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<AuthTokensDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var tokenHash = _tokenService.ComputeTokenHash(request.RefreshToken);
        var now = _dateTimeProvider.UtcNow;

        var existingToken = await _dbContext.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash, cancellationToken);

        if (existingToken is null)
        {
            throw new UnauthorizedException(
                "INVALID_REFRESH_TOKEN",
                "Geçersiz oturum yenileme anahtarı.");
        }

        // Reuse Detection: If a rotated or revoked token is presented again, revoke the entire token family immediately
        if (existingToken.IsRevoked)
        {
            var activeFamilyTokens = await _dbContext.RefreshTokens
                .Where(rt => rt.TokenFamilyId == existingToken.TokenFamilyId && rt.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);

            foreach (var familyToken in activeFamilyTokens)
            {
                familyToken.Revoke(now);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            throw new UnauthorizedException(
                "REFRESH_TOKEN_REUSED",
                "Güvenlik uyarısı: Daha önce kullanılmış bir yenileme anahtarı tespit edildi. Güvenliğiniz için oturum sonlandırıldı.");
        }

        if (existingToken.IsExpired(now))
        {
            existingToken.Revoke(now);
            await _dbContext.SaveChangesAsync(cancellationToken);

            throw new UnauthorizedException(
                "REFRESH_TOKEN_EXPIRED",
                "Oturum süresi doldu. Lütfen tekrar giriş yapın.");
        }

        // Rotate token within the same token family
        var newRefreshResult = _tokenService.GenerateRefreshToken();
        existingToken.Revoke(now, replacedByTokenHash: newRefreshResult.TokenHash);

        var successorToken = RefreshToken.Create(
            existingToken.UserId,
            newRefreshResult.TokenHash,
            newRefreshResult.ExpiresAtUtc,
            existingToken.TokenFamilyId);

        _dbContext.RefreshTokens.Add(successorToken);

        var accessToken = _tokenService.GenerateAccessToken(existingToken.User);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthTokensDto(
            accessToken.AccessToken,
            accessToken.ExpiresAtUtc,
            newRefreshResult.RawToken,
            newRefreshResult.ExpiresAtUtc);
    }
}
