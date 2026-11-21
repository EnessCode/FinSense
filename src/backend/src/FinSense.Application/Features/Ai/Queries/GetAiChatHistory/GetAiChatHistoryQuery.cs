using System.Text.Json;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Ai.Contracts;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Ai.Queries.GetAiChatHistory;

public sealed record GetAiChatHistoryQuery(Guid WorkspaceId) : IRequest<AiChatHistoryResponseDto>;

public sealed class GetAiChatHistoryQueryHandler
    : IRequestHandler<GetAiChatHistoryQuery, AiChatHistoryResponseDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly IAiQuotaService _aiQuotaService;

    public GetAiChatHistoryQueryHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        IAiQuotaService aiQuotaService)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _aiQuotaService = aiQuotaService;
    }

    public async Task<AiChatHistoryResponseDto> Handle(
        GetAiChatHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Viewer,
            cancellationToken);

        var remainingQuota = _aiQuotaService.GetRemainingDailyQuota(access.UserId, AiQuotaType.Chat);

        var conversation = await _dbContext.AiConversations
            .AsNoTracking()
            .Where(c => c.WorkspaceId == request.WorkspaceId && c.UserId == access.UserId)
            .OrderByDescending(c => c.UpdatedAtUtc)
            .Select(c => new { c.Id, c.Title })
            .FirstOrDefaultAsync(cancellationToken);

        if (conversation is null)
        {
            return new AiChatHistoryResponseDto(
                ConversationId: null,
                Title: null,
                RemainingDailyQuota: remainingQuota,
                Messages: []);
        }

        var rawMessages = await _dbContext.AiMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.CreatedAtUtc)
            .ThenBy(m => m.Role)
            .Select(m => new
            {
                m.Id,
                m.Role,
                m.Content,
                m.ReferencedMetrics,
                m.ProviderUsed,
                m.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var messages = rawMessages
            .Select(m =>
            {
                var metadata = TryDeserializeMetadata(m.ReferencedMetrics);
                var role = m.Role == AiMessageRole.User ? "user" : "assistant";

                return new AiChatMessageDto(
                    m.Id,
                    role,
                    m.Content,
                    metadata?.Intent,
                    metadata?.IsFinanceRelated,
                    metadata?.HighlightedCategory,
                    metadata?.SuggestedAction,
                    m.ProviderUsed,
                    m.CreatedAtUtc);
            })
            .ToList();

        return new AiChatHistoryResponseDto(
            conversation.Id,
            conversation.Title,
            remainingQuota,
            messages);
    }

    private static AiMessageMetadata? TryDeserializeMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AiMessageMetadata>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
