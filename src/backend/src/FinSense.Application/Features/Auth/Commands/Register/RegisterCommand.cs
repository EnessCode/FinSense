using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Features.Auth.Contracts;
using FinSense.Domain.Entities;
using FinSense.Domain.Services;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Auth.Commands.Register;

public record RegisterCommand(
    string Email,
    string Password,
    string DisplayName,
    string PreferredLocale = "tr-TR") : IRequest<AuthResponse>;

public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    private static readonly string[] SupportedLocales = ["tr-TR", "en-US"];

    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-posta adresi zorunludur.")
            .MaximumLength(256).WithMessage("E-posta adresi en fazla 256 karakter olabilir.")
            .EmailAddress().WithMessage("Geçerli bir e-posta adresi giriniz.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Şifre zorunludur.")
            .MinimumLength(8).WithMessage("Şifre en az 8 karakter uzunluğunda olmalıdır.")
            .MaximumLength(128).WithMessage("Şifre en fazla 128 karakter olabilir.")
            .Matches("[A-Z]").WithMessage("Şifre en az bir büyük harf içermelidir.")
            .Matches("[a-z]").WithMessage("Şifre en az bir küçük harf içermelidir.")
            .Matches("[0-9]").WithMessage("Şifre en az bir rakam içermelidir.");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Görünen ad zorunludur.")
            .MaximumLength(100).WithMessage("Görünen ad en fazla 100 karakter olabilir.");

        RuleFor(x => x.PreferredLocale)
            .NotEmpty()
            .Must(locale => SupportedLocales.Contains(locale))
            .WithMessage("Desteklenen dil tercihleri: tr-TR, en-US.");
    }
}

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, AuthResponse>
{
    private readonly IAppDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RegisterCommandHandler(
        IAppDbContext dbContext,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<AuthResponse> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var emailExists = await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (emailExists)
        {
            throw new ConflictException(
                "EMAIL_ALREADY_REGISTERED",
                "Bu e-posta adresi ile kayıtlı bir kullanıcı zaten mevcut.");
        }

        var now = _dateTimeProvider.UtcNow;
        var passwordHash = _passwordHasher.HashPassword(request.Password);

        // 1. Create User
        var user = User.Create(
            normalizedEmail,
            passwordHash,
            request.DisplayName,
            request.PreferredLocale,
            now);

        // 2 & 3. Create Personal Workspace and add Owner membership
        var personalWorkspaceName = string.Equals(user.PreferredLocale, "en-US", StringComparison.OrdinalIgnoreCase)
            ? "Personal Space"
            : "Kişisel Alanım";

        var personalWorkspace = Workspace.CreatePersonal(
            user.Id,
            name: personalWorkspaceName,
            defaultCurrency: "TRY",
            utcNow: now);

        // 4. Seed default localized system categories (Expense & Income)
        var defaultCategories = DefaultCategoryCatalog.CreateForWorkspace(personalWorkspace.Id);

        // 5. Issue JWT Access Token and rotating Refresh Token
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshTokenResult = _tokenService.GenerateRefreshToken();
        var refreshTokenEntity = RefreshToken.Create(
            user.Id,
            refreshTokenResult.TokenHash,
            refreshTokenResult.ExpiresAtUtc);

        _dbContext.Users.Add(user);
        _dbContext.Workspaces.Add(personalWorkspace);
        _dbContext.Categories.AddRange(defaultCategories);
        _dbContext.RefreshTokens.Add(refreshTokenEntity);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            new AuthUserDto(
                user.Id,
                user.Email,
                user.DisplayName,
                user.PreferredLocale,
                personalWorkspace.Id),
            new AuthTokensDto(
                accessToken.AccessToken,
                accessToken.ExpiresAtUtc,
                refreshTokenResult.RawToken,
                refreshTokenResult.ExpiresAtUtc));
    }
}
