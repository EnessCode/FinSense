using FinSense.Application.Common.Interfaces;
using FinSense.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FinSense.Infrastructure.Services;

public class DevelopmentLogEmailService : IEmailService
{
    private readonly ILogger<DevelopmentLogEmailService> _logger;

    public DevelopmentLogEmailService(ILogger<DevelopmentLogEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendWorkspaceInvitationAsync(
        string recipientEmail,
        string workspaceName,
        string invitationCode,
        WorkspaceRole assignedRole,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[DevEmailService] Ev Bütçesi Daveti Gönderildi -> Alıcı: {RecipientEmail}, Çalışma Alanı: {WorkspaceName}, Kod: {InvitationCode}, Rol: {AssignedRole}, Son Geçerlilik: {ExpiresAtUtc:O}",
            recipientEmail,
            workspaceName,
            invitationCode,
            assignedRole,
            expiresAtUtc);

        return Task.CompletedTask;
    }
}
