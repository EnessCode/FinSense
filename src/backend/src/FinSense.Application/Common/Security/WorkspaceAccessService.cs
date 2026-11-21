using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Common.Security;

public sealed record WorkspaceAccess(
    Guid WorkspaceId,
    Guid UserId,
    WorkspaceType WorkspaceType,
    WorkspaceRole Role)
{
    public bool IsHousehold => WorkspaceType == WorkspaceType.Household;
}

/// <summary>
/// Defense-in-depth workspace authorization for handlers. On HTTP requests it reuses the membership already
/// resolved by <c>RequireWorkspaceRoleAttribute</c> (via <see cref="IWorkspaceContext"/>), so no extra query is issued;
/// when invoked outside that pipeline (tests, background jobs) it queries <c>workspace_members</c> itself.
/// </summary>
public interface IWorkspaceAccessService
{
    Task<WorkspaceAccess> EnsureAccessAsync(
        Guid workspaceId,
        WorkspaceRole minimumRole,
        CancellationToken cancellationToken = default);
}

public sealed class WorkspaceAccessService : IWorkspaceAccessService
{
    private readonly IAppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IWorkspaceContext _workspaceContext;

    public WorkspaceAccessService(
        IAppDbContext dbContext,
        ICurrentUserService currentUserService,
        IWorkspaceContext workspaceContext)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _workspaceContext = workspaceContext;
    }

    public async Task<WorkspaceAccess> EnsureAccessAsync(
        Guid workspaceId,
        WorkspaceRole minimumRole,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var isEnglish = string.Equals(_currentUserService.Locale, "en-US", StringComparison.OrdinalIgnoreCase);

        WorkspaceType workspaceType;
        WorkspaceRole role;

        if (_workspaceContext.IsInitialized && _workspaceContext.WorkspaceId == workspaceId)
        {
            workspaceType = _workspaceContext.WorkspaceType;
            role = _workspaceContext.Role;
        }
        else
        {
            var membership = await _dbContext.WorkspaceMembers
                .AsNoTracking()
                .Where(wm => wm.WorkspaceId == workspaceId && wm.UserId == userId)
                .Select(wm => new { WorkspaceType = wm.Workspace.Type, wm.Role })
                .FirstOrDefaultAsync(cancellationToken);

            if (membership is null)
            {
                throw new ForbiddenAccessException(
                    "WORKSPACE_ACCESS_DENIED",
                    isEnglish
                        ? "You do not have access to this workspace."
                        : "Bu çalışma alanına erişim yetkiniz bulunmuyor.");
            }

            workspaceType = membership.WorkspaceType;
            role = membership.Role;
            _workspaceContext.Initialize(workspaceId, workspaceType, role);
        }

        if (!role.HasAtLeast(minimumRole))
        {
            throw new ForbiddenAccessException(
                "WORKSPACE_INSUFFICIENT_ROLE",
                isEnglish
                    ? $"You must have at least the '{minimumRole}' role in this workspace to perform this operation."
                    : $"Bu çalışma alanında bu işlemi gerçekleştirmek için en az '{minimumRole}' yetkisine sahip olmalısınız.");
        }

        return new WorkspaceAccess(workspaceId, userId, workspaceType, role);
    }
}
