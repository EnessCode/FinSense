using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Features.Workspaces.Contracts;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Workspaces.Commands.UpdateMemberRole;

public record UpdateWorkspaceMemberRoleCommand(
    Guid WorkspaceId,
    Guid TargetUserId,
    WorkspaceRole NewRole) : IRequest<WorkspaceMemberDto>;

public class UpdateWorkspaceMemberRoleCommandValidator : AbstractValidator<UpdateWorkspaceMemberRoleCommand>
{
    public UpdateWorkspaceMemberRoleCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.TargetUserId).NotEmpty();
        RuleFor(x => x.NewRole)
            .IsInEnum()
            .Must(role => role != WorkspaceRole.Owner)
            .WithMessage("Üye rolü yalnızca Admin, Contributor veya Viewer olarak güncellenebilir.");
    }
}

public class UpdateWorkspaceMemberRoleCommandHandler
    : IRequestHandler<UpdateWorkspaceMemberRoleCommand, WorkspaceMemberDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateWorkspaceMemberRoleCommandHandler(
        IAppDbContext dbContext,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<WorkspaceMemberDto> Handle(
        UpdateWorkspaceMemberRoleCommand request,
        CancellationToken cancellationToken)
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

        if (!actorMembership.Role.CanManageMembers())
        {
            throw new ForbiddenAccessException(
                "WORKSPACE_INSUFFICIENT_ROLE",
                "Üye rollerini değiştirmek için Yönetici (Admin) veya Kurucu (Owner) yetkisine sahip olmalısınız.");
        }

        var targetMember = await _dbContext.WorkspaceMembers
            .Include(wm => wm.User)
            .FirstOrDefaultAsync(
                wm => wm.WorkspaceId == request.WorkspaceId && wm.UserId == request.TargetUserId,
                cancellationToken);

        if (targetMember is null)
        {
            throw new NotFoundException(
                "WORKSPACE_MEMBER_NOT_FOUND",
                "Belirtilen kullanıcı bu çalışma alanında üye değil.");
        }

        if (targetMember.Role == WorkspaceRole.Owner)
        {
            throw new DomainException(
                "CANNOT_MODIFY_WORKSPACE_OWNER",
                "Çalışma alanı kurucusunun (Owner) rolü değiştirilemez.");
        }

        var previousRole = targetMember.Role;
        targetMember.ChangeRole(request.NewRole);

        var auditLog = AuditLog.Create(
            request.WorkspaceId,
            actorUserId,
            "MemberRoleChanged",
            nameof(WorkspaceMember),
            targetMember.Id,
            oldValuesJson: $"{{\"role\":\"{previousRole}\"}}",
            newValuesJson: $"{{\"role\":\"{targetMember.Role}\"}}",
            utcNow: _dateTimeProvider.UtcNow);

        _dbContext.AuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new WorkspaceMemberDto(
            targetMember.UserId,
            targetMember.User.DisplayName,
            targetMember.User.Email,
            targetMember.Role,
            targetMember.JoinedAtUtc);
    }
}
