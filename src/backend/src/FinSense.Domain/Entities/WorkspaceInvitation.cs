using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;

namespace FinSense.Domain.Entities;

public class WorkspaceInvitation
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public string InvitationCode { get; private set; } = string.Empty;
    public string? InvitedEmail { get; private set; }
    public WorkspaceRole AssignedRole { get; private set; }
    public Guid InvitedByUserId { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? AcceptedAtUtc { get; private set; }
    public Guid? AcceptedByUserId { get; private set; }

    public Workspace Workspace { get; private set; } = null!;
    public User InvitedByUser { get; private set; } = null!;

    private WorkspaceInvitation() { }

    public static WorkspaceInvitation Create(
        Guid workspaceId,
        string invitationCode,
        WorkspaceRole assignedRole,
        Guid invitedByUserId,
        DateTime expiresAtUtc,
        string? invitedEmail = null)
    {
        if (workspaceId == Guid.Empty)
        {
            throw new DomainException("INVALID_WORKSPACE_ID", "Geçerli bir çalışma alanı kimliği gereklidir.");
        }

        if (string.IsNullOrWhiteSpace(invitationCode) || invitationCode.Trim().Length != 8)
        {
            throw new DomainException("INVALID_INVITATION_CODE", "Davet kodu 8 karakterli alfanümerik olmalıdır.");
        }

        if (!Enum.IsDefined(assignedRole) || assignedRole == WorkspaceRole.Owner)
        {
            throw new DomainException("INVALID_INVITATION_ROLE", "Davet ile yalnızca Admin, Contributor veya Viewer rolü atanabilir.");
        }

        if (invitedByUserId == Guid.Empty)
        {
            throw new DomainException("INVALID_INVITER_USER_ID", "Davet eden kullanıcı kimliği gereklidir.");
        }

        return new WorkspaceInvitation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            InvitationCode = invitationCode.Trim().ToUpperInvariant(),
            InvitedEmail = string.IsNullOrWhiteSpace(invitedEmail) ? null : invitedEmail.Trim().ToLowerInvariant(),
            AssignedRole = assignedRole,
            InvitedByUserId = invitedByUserId,
            ExpiresAtUtc = expiresAtUtc
        };
    }

    public void Accept(Guid acceptingUserId, DateTime utcNow)
    {
        if (acceptingUserId == Guid.Empty)
        {
            throw new DomainException("INVALID_USER_ID", "Geçerli bir kullanıcı kimliği gereklidir.");
        }

        if (AcceptedAtUtc.HasValue)
        {
            throw new DomainException("INVITATION_ALREADY_ACCEPTED", "Bu davet kodu daha önce kullanılmış.");
        }

        if (utcNow >= ExpiresAtUtc)
        {
            throw new DomainException("INVITATION_EXPIRED", "Bu davet kodunun süresi dolmuş.");
        }

        AcceptedByUserId = acceptingUserId;
        AcceptedAtUtc = utcNow;
    }
}
