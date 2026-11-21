using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;

namespace FinSense.Domain.Entities;

public class WorkspaceMember
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }
    public WorkspaceRole Role { get; private set; }
    public DateTime JoinedAtUtc { get; private set; }

    public Workspace Workspace { get; private set; } = null!;
    public User User { get; private set; } = null!;

    private WorkspaceMember() { }

    public static WorkspaceMember Create(
        Guid workspaceId,
        Guid userId,
        WorkspaceRole role,
        DateTime? joinedAtUtc = null)
    {
        if (workspaceId == Guid.Empty)
        {
            throw new DomainException("INVALID_WORKSPACE_ID", "Geçerli bir çalışma alanı kimliği gereklidir.");
        }

        if (userId == Guid.Empty)
        {
            throw new DomainException("INVALID_USER_ID", "Geçerli bir kullanıcı kimliği gereklidir.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new DomainException("INVALID_WORKSPACE_ROLE", "Geçersiz çalışma alanı rolü.");
        }

        return new WorkspaceMember
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            Role = role,
            JoinedAtUtc = joinedAtUtc ?? DateTime.UtcNow
        };
    }

    public void ChangeRole(WorkspaceRole newRole)
    {
        if (!Enum.IsDefined(newRole))
        {
            throw new DomainException("INVALID_WORKSPACE_ROLE", "Geçersiz çalışma alanı rolü.");
        }

        if (Role == WorkspaceRole.Owner && newRole != WorkspaceRole.Owner)
        {
            throw new DomainException("CANNOT_DEMOTE_WORKSPACE_OWNER", "Çalışma alanı kurucusunun (Owner) rolü düşürülemez.");
        }

        Role = newRole;
    }
}
