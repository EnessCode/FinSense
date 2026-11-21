using FinSense.Domain.Exceptions;

namespace FinSense.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string ActionType { get; private set; } = string.Empty;
    public string EntityName { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public string? OldValues { get; private set; }
    public string? NewValues { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public Workspace Workspace { get; private set; } = null!;
    public User? ActorUser { get; private set; }

    private AuditLog() { }

    public static AuditLog Create(
        Guid workspaceId,
        Guid? actorUserId,
        string actionType,
        string entityName,
        Guid entityId,
        string? oldValuesJson = null,
        string? newValuesJson = null,
        DateTime? utcNow = null)
    {
        if (workspaceId == Guid.Empty)
        {
            throw new DomainException("INVALID_WORKSPACE_ID", "Geçerli bir çalışma alanı kimliği gereklidir.");
        }

        if (string.IsNullOrWhiteSpace(actionType))
        {
            throw new DomainException("AUDIT_ACTION_TYPE_REQUIRED", "Denetim eylem türü zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(entityName))
        {
            throw new DomainException("AUDIT_ENTITY_NAME_REQUIRED", "Denetim varlık adı zorunludur.");
        }

        if (entityId == Guid.Empty)
        {
            throw new DomainException("AUDIT_ENTITY_ID_REQUIRED", "Denetim varlık kimliği zorunludur.");
        }

        return new AuditLog
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ActorUserId = actorUserId,
            ActionType = actionType.Trim(),
            EntityName = entityName.Trim(),
            EntityId = entityId,
            OldValues = string.IsNullOrWhiteSpace(oldValuesJson) ? null : oldValuesJson,
            NewValues = string.IsNullOrWhiteSpace(newValuesJson) ? null : newValuesJson,
            CreatedAtUtc = utcNow ?? DateTime.UtcNow
        };
    }
}
