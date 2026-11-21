using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Features.Workspaces.Contracts;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Workspaces.Commands.AcceptInvitation;

public record AcceptWorkspaceInvitationCommand(string InvitationCode) : IRequest<WorkspaceSummaryDto>;

public class AcceptWorkspaceInvitationCommandValidator : AbstractValidator<AcceptWorkspaceInvitationCommand>
{
    public AcceptWorkspaceInvitationCommandValidator()
    {
        RuleFor(x => x.InvitationCode)
            .NotEmpty().WithMessage("Davet kodu zorunludur.")
            .Length(8).WithMessage("Davet kodu 8 karakter uzunluğunda olmalıdır.");
    }
}

public class AcceptWorkspaceInvitationCommandHandler
    : IRequestHandler<AcceptWorkspaceInvitationCommand, WorkspaceSummaryDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AcceptWorkspaceInvitationCommandHandler(
        IAppDbContext dbContext,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<WorkspaceSummaryDto> Handle(
        AcceptWorkspaceInvitationCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var normalizedCode = request.InvitationCode.Trim().ToUpperInvariant();
        var now = _dateTimeProvider.UtcNow;

        var invitation = await _dbContext.WorkspaceInvitations
            .Include(wi => wi.Workspace)
            .FirstOrDefaultAsync(wi => wi.InvitationCode == normalizedCode, cancellationToken);

        if (invitation is null)
        {
            throw new NotFoundException(
                "INVITATION_NOT_FOUND",
                "Geçersiz veya bulunamayan davet kodu.");
        }

        if (invitation.Workspace.Type == WorkspaceType.Personal)
        {
            throw new DomainException(
                "PERSONAL_WORKSPACE_INVITATION_FORBIDDEN",
                "Kişisel çalışma alanına katılım yapılamaz.");
        }

        // Domain invariant check: validates not already accepted and not expired
        invitation.Accept(userId, now);

        var alreadyMember = await _dbContext.WorkspaceMembers
            .AsNoTracking()
            .AnyAsync(
                wm => wm.WorkspaceId == invitation.WorkspaceId && wm.UserId == userId,
                cancellationToken);

        if (alreadyMember)
        {
            throw new DomainException(
                "ALREADY_WORKSPACE_MEMBER",
                "Bu çalışma alanına zaten üyesiniz.");
        }

        var member = WorkspaceMember.Create(
            invitation.WorkspaceId,
            userId,
            invitation.AssignedRole,
            now);

        var auditLog = AuditLog.Create(
            invitation.WorkspaceId,
            userId,
            "MemberJoinedViaInvitation",
            nameof(WorkspaceMember),
            member.Id,
            oldValuesJson: null,
            newValuesJson: $"{{\"userId\":\"{userId}\",\"role\":\"{member.Role}\"}}",
            utcNow: now);

        _dbContext.WorkspaceMembers.Add(member);
        _dbContext.AuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new WorkspaceSummaryDto(
            invitation.Workspace.Id,
            invitation.Workspace.Name,
            invitation.Workspace.Type,
            invitation.Workspace.DefaultCurrency,
            member.Role,
            invitation.Workspace.CreatedAtUtc,
            now);
    }
}
