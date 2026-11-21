using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Features.Users.Contracts;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Users.Commands.UpdateUserProfile;

public record UpdateUserProfileCommand(
    string DisplayName,
    string PreferredLocale) : IRequest<UserProfileDto>;

public class UpdateUserProfileCommandValidator : AbstractValidator<UpdateUserProfileCommand>
{
    private static readonly string[] SupportedLocales = ["tr-TR", "en-US"];

    public UpdateUserProfileCommandValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Görünen ad zorunludur.")
            .MaximumLength(100).WithMessage("Görünen ad en fazla 100 karakter olabilir.");

        RuleFor(x => x.PreferredLocale)
            .NotEmpty().WithMessage("Dil tercihi zorunludur.")
            .Must(locale => SupportedLocales.Contains(locale))
            .WithMessage("Desteklenen dil tercihleri: tr-TR, en-US.");
    }
}

public class UpdateUserProfileCommandHandler : IRequestHandler<UpdateUserProfileCommand, UserProfileDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateUserProfileCommandHandler(
        IAppDbContext dbContext,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<UserProfileDto> Handle(UpdateUserProfileCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            throw new NotFoundException("USER_NOT_FOUND", "Kullanıcı bulunamadı.");
        }

        user.UpdateProfile(request.DisplayName, request.PreferredLocale, _dateTimeProvider.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var memberships = await _dbContext.WorkspaceMembers
            .AsNoTracking()
            .Where(wm => wm.UserId == userId)
            .OrderBy(wm => wm.Workspace.Type)
            .ThenBy(wm => wm.JoinedAtUtc)
            .Select(wm => new UserWorkspaceMembershipDto(
                wm.WorkspaceId,
                wm.Workspace.Name,
                wm.Workspace.Type,
                wm.Workspace.DefaultCurrency,
                wm.Role,
                wm.JoinedAtUtc))
            .ToListAsync(cancellationToken);

        return new UserProfileDto(
            user.Id,
            user.Email,
            user.DisplayName,
            user.PreferredLocale,
            user.CreatedAtUtc,
            user.UpdatedAtUtc,
            memberships);
    }
}
