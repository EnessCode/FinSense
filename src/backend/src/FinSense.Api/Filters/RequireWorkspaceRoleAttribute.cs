using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Api.Filters;

/// <summary>
/// Enforces Zero-Trust workspace membership and role-based access control on routes
/// containing <c>{wsId}</c> or <c>{workspaceId}</c>, and populates <see cref="IWorkspaceContext"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireWorkspaceRoleAttribute : TypeFilterAttribute
{
    public RequireWorkspaceRoleAttribute(WorkspaceRole minimumRole = WorkspaceRole.Viewer)
        : base(typeof(WorkspaceRoleAuthorizationFilter))
    {
        Arguments = [minimumRole];
    }
}

public class WorkspaceRoleAuthorizationFilter : IAsyncActionFilter
{
    private readonly WorkspaceRole _minimumRole;
    private readonly IAppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IWorkspaceContext _workspaceContext;

    public WorkspaceRoleAuthorizationFilter(
        WorkspaceRole minimumRole,
        IAppDbContext dbContext,
        ICurrentUserService currentUserService,
        IWorkspaceContext workspaceContext)
    {
        _minimumRole = minimumRole;
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _workspaceContext = workspaceContext;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var userId = _currentUserService.GetRequiredUserId();

        if (!TryResolveWorkspaceId(context, out var workspaceId))
        {
            throw new NotFoundException(
                "WORKSPACE_NOT_FOUND",
                "Çalışma alanı kimliği (wsId) bulunamadı.");
        }

        // O(1) index lookup via IX_workspace_members_workspace_user with AsNoTracking()
        var membership = await _dbContext.WorkspaceMembers
            .AsNoTracking()
            .Where(wm => wm.WorkspaceId == workspaceId && wm.UserId == userId)
            .Select(wm => new
            {
                wm.WorkspaceId,
                WorkspaceType = wm.Workspace.Type,
                wm.Role
            })
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (membership is null)
        {
            var isEnglish = string.Equals(_currentUserService.Locale, "en-US", StringComparison.OrdinalIgnoreCase);
            throw new ForbiddenAccessException(
                "WORKSPACE_ACCESS_DENIED",
                isEnglish
                    ? "You do not have access to this workspace."
                    : "Bu çalışma alanına erişim yetkiniz bulunmuyor.");
        }

        if (!membership.Role.HasAtLeast(_minimumRole))
        {
            var isEnglish = string.Equals(_currentUserService.Locale, "en-US", StringComparison.OrdinalIgnoreCase);
            throw new ForbiddenAccessException(
                "WORKSPACE_INSUFFICIENT_ROLE",
                isEnglish
                    ? $"You must have at least the '{_minimumRole}' role in this workspace to perform this operation."
                    : $"Bu çalışma alanında bu işlemi gerçekleştirmek için en az '{_minimumRole}' yetkisine sahip olmalısınız.");
        }

        _workspaceContext.Initialize(membership.WorkspaceId, membership.WorkspaceType, membership.Role);

        await next();
    }

    private static bool TryResolveWorkspaceId(ActionExecutingContext context, out Guid workspaceId)
    {
        var routeValues = context.RouteData.Values;

        if ((routeValues.TryGetValue("wsId", out var rawWsId) ||
             routeValues.TryGetValue("workspaceId", out rawWsId)) &&
            rawWsId is not null &&
            Guid.TryParse(rawWsId.ToString(), out workspaceId) &&
            workspaceId != Guid.Empty)
        {
            return true;
        }

        workspaceId = Guid.Empty;
        return false;
    }
}
