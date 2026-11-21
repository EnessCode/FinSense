using System.Security.Cryptography;
using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Features.Workspaces.Contracts;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Workspaces.Commands.CreateInvitation;

public record CreateWorkspaceInvitationCommand(
    Guid WorkspaceId,
    WorkspaceRole AssignedRole,
    string? InvitedEmail = null) : IRequest<WorkspaceInvitationDto>;

public class CreateWorkspaceInvitationCommandValidator : AbstractValidator<CreateWorkspaceInvitationCommand>
{
    public CreateWorkspaceInvitationCommandValidator()
    {
        RuleFor(x => x.WorkspaceId)
            .NotEmpty().WithMessage("Çalışma alanı kimliği zorunludur.");

        RuleFor(x => x.AssignedRole)
            .IsInEnum()
            .Must(role => role != WorkspaceRole.Owner)
            .WithMessage("Davet ile yalnızca Admin, Contributor veya Viewer rolü atanabilir.");

        RuleFor(x => x.InvitedEmail)
            .EmailAddress()
            .MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.InvitedEmail))
            .WithMessage("Geçerli bir e-posta adresi giriniz.");
    }
}

public class CreateWorkspaceInvitationCommandHandler
    : IRequestHandler<CreateWorkspaceInvitationCommand, WorkspaceInvitationDto>
{
    private const string InvitationAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly IAppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IEmailService _emailService;

    public CreateWorkspaceInvitationCommandHandler(
        IAppDbContext dbContext,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider,
        IEmailService emailService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
        _emailService = emailService;
    }

    public async Task<WorkspaceInvitationDto> Handle(
        CreateWorkspaceInvitationCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();

        var membership = await _dbContext.WorkspaceMembers
            .AsNoTracking()
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

        if (membership.Workspace.Type == WorkspaceType.Personal)
        {
            throw new DomainException(
                "PERSONAL_WORKSPACE_INVITATION_FORBIDDEN",
                "Kişisel çalışma alanına üye davet edilemez.");
        }

        if (!membership.Role.CanManageMembers())
        {
            throw new ForbiddenAccessException(
                "WORKSPACE_INSUFFICIENT_ROLE",
                "Üye davet etmek için Yönetici (Admin) veya Kurucu (Owner) yetkisine sahip olmalısınız.");
        }

        var code = await GenerateUniqueInvitationCodeAsync(cancellationToken);
        var now = _dateTimeProvider.UtcNow;
        var expiresAtUtc = now.AddHours(48);

        var invitation = WorkspaceInvitation.Create(
            request.WorkspaceId,
            code,
            request.AssignedRole,
            userId,
            expiresAtUtc,
            request.InvitedEmail);

        var auditLog = AuditLog.Create(
            request.WorkspaceId,
            userId,
            "WorkspaceInvitationCreated",
            nameof(WorkspaceInvitation),
            invitation.Id,
            oldValuesJson: null,
            newValuesJson: $"{{\"assignedRole\":\"{invitation.AssignedRole}\",\"expiresAtUtc\":\"{expiresAtUtc:O}\"}}",
            utcNow: now);

        _dbContext.WorkspaceInvitations.Add(invitation);
        _dbContext.AuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(invitation.InvitedEmail))
        {
            await _emailService.SendWorkspaceInvitationAsync(
                invitation.InvitedEmail,
                membership.Workspace.Name,
                invitation.InvitationCode,
                invitation.AssignedRole,
                invitation.ExpiresAtUtc,
                cancellationToken);
        }

        return new WorkspaceInvitationDto(
            invitation.Id,
            invitation.WorkspaceId,
            invitation.InvitationCode,
            invitation.InvitedEmail,
            invitation.AssignedRole,
            invitation.ExpiresAtUtc);
    }

    private async Task<string> GenerateUniqueInvitationCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var chars = new char[8];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = InvitationAlphabet[RandomNumberGenerator.GetInt32(InvitationAlphabet.Length)];
            }

            var candidate = new string(chars);
            var exists = await _dbContext.WorkspaceInvitations
                .AsNoTracking()
                .AnyAsync(wi => wi.InvitationCode == candidate, cancellationToken);

            if (!exists)
            {
                return candidate;
            }
        }

        throw new DomainException("INVITATION_CODE_GENERATION_FAILED", "Benzersiz davet kodu üretilemedi.");
    }
}
