using FinSense.Application.Common.Interfaces;
using FinSense.Application.Features.Workspaces.Contracts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Workspaces.Queries.GetUserWorkspaces;

public record GetUserWorkspacesQuery : IRequest<IReadOnlyList<WorkspaceSummaryDto>>;

public class GetUserWorkspacesQueryHandler
    : IRequestHandler<GetUserWorkspacesQuery, IReadOnlyList<WorkspaceSummaryDto>>
{
    private readonly IAppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public GetUserWorkspacesQueryHandler(
        IAppDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<IReadOnlyList<WorkspaceSummaryDto>> Handle(
        GetUserWorkspacesQuery request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();

        return await _dbContext.WorkspaceMembers
            .AsNoTracking()
            .Where(wm => wm.UserId == userId)
            .OrderBy(wm => wm.Workspace.Type)
            .ThenBy(wm => wm.JoinedAtUtc)
            .Select(wm => new WorkspaceSummaryDto(
                wm.WorkspaceId,
                wm.Workspace.Name,
                wm.Workspace.Type,
                wm.Workspace.DefaultCurrency,
                wm.Role,
                wm.Workspace.CreatedAtUtc,
                wm.JoinedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
