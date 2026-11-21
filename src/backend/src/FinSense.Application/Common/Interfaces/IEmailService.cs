using FinSense.Domain.Enums;

namespace FinSense.Application.Common.Interfaces;

public interface IEmailService
{
    Task SendWorkspaceInvitationAsync(
        string recipientEmail,
        string workspaceName,
        string invitationCode,
        WorkspaceRole assignedRole,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default);
}
