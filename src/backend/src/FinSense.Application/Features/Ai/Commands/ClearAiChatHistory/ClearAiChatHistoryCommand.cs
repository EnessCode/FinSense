using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Ai.Commands.ClearAiChatHistory;

public sealed record ClearAiChatHistoryCommand(Guid WorkspaceId) : IRequest;

public sealed class ClearAiChatHistoryCommandHandler : IRequestHandler<ClearAiChatHistoryCommand>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;

    public ClearAiChatHistoryCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
    }

    public async Task Handle(ClearAiChatHistoryCommand request, CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Viewer,
            cancellationToken);

        var conversations = await _dbContext.AiConversations
            .Where(c => c.WorkspaceId == request.WorkspaceId && c.UserId == access.UserId)
            .ToListAsync(cancellationToken);

        if (conversations.Count == 0)
        {
            return;
        }

        var conversationIds = conversations.Select(c => c.Id).ToList();

        var messages = await _dbContext.AiMessages
            .Where(m => conversationIds.Contains(m.ConversationId))
            .ToListAsync(cancellationToken);

        if (messages.Count > 0)
        {
            _dbContext.AiMessages.RemoveRange(messages);
        }

        _dbContext.AiConversations.RemoveRange(conversations);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
