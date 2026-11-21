using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Workspaces.Commands.DeleteHouseholdWorkspace;

public record DeleteHouseholdWorkspaceCommand(Guid WorkspaceId) : IRequest;

public class DeleteHouseholdWorkspaceCommandHandler : IRequestHandler<DeleteHouseholdWorkspaceCommand>
{
    private readonly IAppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public DeleteHouseholdWorkspaceCommandHandler(
        IAppDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task Handle(DeleteHouseholdWorkspaceCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();

        var membership = await _dbContext.WorkspaceMembers
            .Include(wm => wm.Workspace)
            .FirstOrDefaultAsync(
                wm => wm.WorkspaceId == request.WorkspaceId && wm.UserId == userId,
                cancellationToken);

        if (membership is null)
        {
            throw new ForbiddenAccessException(
                "WORKSPACE_ACCESS_DENIED",
                "Bu çalışma alanına erişim yetkiniz bulunmuyor.");
        }

        // Personal workspace cannot be deleted independently of account deletion
        membership.Workspace.EnsureCanBeDeletedIndependently();

        if (!membership.Role.CanDeleteHousehold())
        {
            throw new ForbiddenAccessException(
                "WORKSPACE_INSUFFICIENT_ROLE",
                "Ev bütçesi çalışma alanını silmek için Kurucu (Owner) yetkisine sahip olmalısınız.");
        }

        _dbContext.Workspaces.Remove(membership.Workspace);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
