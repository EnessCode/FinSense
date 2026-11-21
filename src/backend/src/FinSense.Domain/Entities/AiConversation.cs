using FinSense.Domain.Exceptions;

namespace FinSense.Domain.Entities;

public class AiConversation
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid UserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public Workspace Workspace { get; private set; } = null!;
    public User User { get; private set; } = null!;

    private readonly List<AiMessage> _messages = [];
    public IReadOnlyCollection<AiMessage> Messages => _messages.AsReadOnly();

    private AiConversation() { }

    public static AiConversation Create(
        Guid workspaceId,
        Guid userId,
        string title,
        DateTime? utcNow = null)
    {
        if (workspaceId == Guid.Empty)
        {
            throw new DomainException("INVALID_WORKSPACE_ID", "Geçerli bir çalışma alanı kimliği gereklidir.");
        }

        if (userId == Guid.Empty)
        {
            throw new DomainException("INVALID_USER_ID", "Geçerli bir kullanıcı kimliği gereklidir.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("CONVERSATION_TITLE_REQUIRED", "Sohbet başlığı zorunludur.");
        }

        var now = utcNow ?? DateTime.UtcNow;

        return new AiConversation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId = userId,
            Title = title.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public AiMessage AddMessage(
        Enums.AiMessageRole role,
        string content,
        string? referencedMetricsJson = null,
        string? providerUsed = null,
        DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        var message = AiMessage.Create(Id, role, content, referencedMetricsJson, providerUsed, now);
        _messages.Add(message);
        UpdatedAtUtc = now;
        return message;
    }
}
