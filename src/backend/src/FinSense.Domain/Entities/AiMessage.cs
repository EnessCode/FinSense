using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;

namespace FinSense.Domain.Entities;

public class AiMessage
{
    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public AiMessageRole Role { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string? ReferencedMetrics { get; private set; }
    public string? ProviderUsed { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public AiConversation Conversation { get; private set; } = null!;

    private AiMessage() { }

    public static AiMessage Create(
        Guid conversationId,
        AiMessageRole role,
        string content,
        string? referencedMetricsJson = null,
        string? providerUsed = null,
        DateTime? utcNow = null)
    {
        if (conversationId == Guid.Empty)
        {
            throw new DomainException("INVALID_CONVERSATION_ID", "Geçerli bir sohbet kimliği gereklidir.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new DomainException("INVALID_MESSAGE_ROLE", "Geçersiz mesaj rolü.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DomainException("MESSAGE_CONTENT_REQUIRED", "Mesaj içeriği boş olamaz.");
        }

        return new AiMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role,
            Content = content.Trim(),
            ReferencedMetrics = string.IsNullOrWhiteSpace(referencedMetricsJson) ? null : referencedMetricsJson,
            ProviderUsed = string.IsNullOrWhiteSpace(providerUsed) ? null : providerUsed.Trim(),
            CreatedAtUtc = utcNow ?? DateTime.UtcNow
        };
    }
}
