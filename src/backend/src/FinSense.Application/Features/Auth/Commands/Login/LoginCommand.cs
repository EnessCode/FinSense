using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Features.Auth.Contracts;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Auth.Commands.Login;

public record LoginCommand(string Email, string Password) : IRequest<AuthResponse>;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-posta adresi zorunludur.")
            .EmailAddress().WithMessage("Geçerli bir e-posta adresi giriniz.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Şifre zorunludur.");
    }
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResponse>
{
    private readonly IAppDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;

    public LoginCommandHandler(
        IAppDbContext dbContext,
        IPasswordHasher passwordHasher,
        ITokenService tokenService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<AuthResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException(
                "INVALID_CREDENTIALS",
                "E-posta adresi veya şifre hatalı.");
        }

        var personalWorkspaceId = await _dbContext.WorkspaceMembers
            .AsNoTracking()
            .Where(wm => wm.UserId == user.Id && wm.Workspace.Type == WorkspaceType.Personal)
            .Select(wm => wm.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshTokenResult = _tokenService.GenerateRefreshToken();
        var refreshTokenEntity = RefreshToken.Create(
            user.Id,
            refreshTokenResult.TokenHash,
            refreshTokenResult.ExpiresAtUtc);

        _dbContext.RefreshTokens.Add(refreshTokenEntity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            new AuthUserDto(
                user.Id,
                user.Email,
                user.DisplayName,
                user.PreferredLocale,
                personalWorkspaceId),
            new AuthTokensDto(
                accessToken.AccessToken,
                accessToken.ExpiresAtUtc,
                refreshTokenResult.RawToken,
                refreshTokenResult.ExpiresAtUtc));
    }
}
