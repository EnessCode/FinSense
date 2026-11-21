using FinSense.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Auth.Commands.Logout;

public record LogoutCommand(string? RefreshToken = null) : IRequest;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand>
{
    private readonly IAppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly ITokenService _tokenService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public LogoutCommandHandler(
        IAppDbContext dbContext,
        ICurrentUserService currentUserService,
        ITokenService tokenService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _tokenService = tokenService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var now = _dateTimeProvider.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            var tokenHash = _tokenService.ComputeTokenHash(request.RefreshToken);
            var targetToken = await _dbContext.RefreshTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(rt => rt.UserId == userId && rt.TokenHash == tokenHash, cancellationToken);

            if (targetToken is not null)
            {
                var familyTokens = await _dbContext.RefreshTokens
                    .Where(rt => rt.UserId == userId &&
                                 rt.TokenFamilyId == targetToken.TokenFamilyId &&
                                 rt.RevokedAtUtc == null)
                    .ToListAsync(cancellationToken);

                foreach (var token in familyTokens)
                {
                    token.Revoke(now);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
        }

        var activeUserTokens = await _dbContext.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in activeUserTokens)
        {
            token.Revoke(now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
