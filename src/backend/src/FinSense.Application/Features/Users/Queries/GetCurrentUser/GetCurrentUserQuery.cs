using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Features.Users.Contracts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Users.Queries.GetCurrentUser;

public record GetCurrentUserQuery : IRequest<UserProfileDto>;

public class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, UserProfileDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public GetCurrentUserQueryHandler(
        IAppDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<UserProfileDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            throw new NotFoundException("USER_NOT_FOUND", "Kullanıcı bulunamadı.");
        }

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
