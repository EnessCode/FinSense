using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Workspaces.Commands.RemoveMember;

public record RemoveWorkspaceMemberCommand(
    Guid WorkspaceId,
    Guid TargetUserId) : IRequest;

public class RemoveWorkspaceMemberCommandHandler : IRequestHandler<RemoveWorkspaceMemberCommand>
{
    private readonly IAppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RemoveWorkspaceMemberCommandHandler(
        IAppDbContext dbContext,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task Handle(RemoveWorkspaceMemberCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = _currentUserService.GetRequiredUserId();

        var actorMembership = await _dbContext.WorkspaceMembers
            .AsNoTracking()
            .Include(wm => wm.Workspace)
            .FirstOrDefaultAsync(
                wm => wm.WorkspaceId == request.WorkspaceId && wm.UserId == actorUserId,
                cancellationToken);

        if (actorMembership is null)
        {
            throw new ForbiddenAccessException(
                "WORKSPACE_ACCESS_DENIED",
                "Bu çalışma alanına erişim yetkiniz bulunmuyor.");
        }

        if (actorMembership.Workspace.Type == WorkspaceType.Personal)
        {
            throw new DomainException(
                "PERSONAL_WORKSPACE_MEMBER_CANNOT_BE_REMOVED",
                "Kişisel çalışma alanından üyelik silinemez.");
        }

        var targetMember = await _dbContext.WorkspaceMembers
            .FirstOrDefaultAsync(
                wm => wm.WorkspaceId == request.WorkspaceId && wm.UserId == request.TargetUserId,
                cancellationToken);

        if (targetMember is null)
        {
            throw new NotFoundException(
                "WORKSPACE_MEMBER_NOT_FOUND",
                "Belirtilen kullanıcı bu çalışma alanında üye değil.");
        }

        var isSelfLeave = actorUserId == request.TargetUserId;

        if (isSelfLeave)
        {
            if (targetMember.Role == WorkspaceRole.Owner)
            {
                throw new DomainException(
                    "OWNER_CANNOT_LEAVE_WORKSPACE",
                    "Çalışma alanı kurucusu (Owner) çalışma alanından ayrılamaz.");
            }
        }
        else
        {
            if (!actorMembership.Role.CanManageMembers())
            {
                throw new ForbiddenAccessException(
                    "WORKSPACE_INSUFFICIENT_ROLE",
                    "Başka bir üyeyi çıkarmak için Yönetici (Admin) veya Kurucu (Owner) yetkisine sahip olmalısınız.");
            }

            if (targetMember.Role == WorkspaceRole.Owner)
            {
                throw new DomainException(
                    "CANNOT_REMOVE_WORKSPACE_OWNER",
                    "Çalışma alanı kurucusu (Owner) çalışma alanından çıkarılamaz.");
            }
        }

        var auditLog = AuditLog.Create(
            request.WorkspaceId,
            actorUserId,
            isSelfLeave ? "MemberLeftWorkspace" : "MemberRemovedFromWorkspace",
            nameof(WorkspaceMember),
            targetMember.Id,
            oldValuesJson: $"{{\"userId\":\"{targetMember.UserId}\",\"role\":\"{targetMember.Role}\"}}",
            newValuesJson: null,
            utcNow: _dateTimeProvider.UtcNow);

        _dbContext.WorkspaceMembers.Remove(targetMember);
        _dbContext.AuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
