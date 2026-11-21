using FinSense.Application.Common.Interfaces;
using FinSense.Application.Features.Workspaces.Contracts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Workspaces.Queries.GetWorkspaceMembers;

public record GetWorkspaceMembersQuery(Guid WorkspaceId) : IRequest<IReadOnlyList<WorkspaceMemberDto>>;

public class GetWorkspaceMembersQueryHandler
    : IRequestHandler<GetWorkspaceMembersQuery, IReadOnlyList<WorkspaceMemberDto>>
{
    private readonly IAppDbContext _dbContext;

    public GetWorkspaceMembersQueryHandler(IAppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<WorkspaceMemberDto>> Handle(
        GetWorkspaceMembersQuery request,
        CancellationToken cancellationToken)
    {
        return await _dbContext.WorkspaceMembers
            .AsNoTracking()
            .Where(wm => wm.WorkspaceId == request.WorkspaceId)
            .OrderBy(wm => wm.Role)
            .ThenBy(wm => wm.JoinedAtUtc)
            .Select(wm => new WorkspaceMemberDto(
                wm.UserId,
                wm.User.DisplayName,
                wm.User.Email,
                wm.Role,
                wm.JoinedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
