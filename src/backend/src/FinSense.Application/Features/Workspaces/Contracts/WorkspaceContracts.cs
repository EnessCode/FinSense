using FinSense.Domain.Enums;

namespace FinSense.Application.Features.Workspaces.Contracts;

public record WorkspaceSummaryDto(
    Guid Id,
    string Name,
    WorkspaceType Type,
    string DefaultCurrency,
    WorkspaceRole CurrentUserRole,
    DateTime CreatedAtUtc,
    DateTime JoinedAtUtc);

public record WorkspaceMemberDto(
    Guid UserId,
    string DisplayName,
    string Email,
    WorkspaceRole Role,
    DateTime JoinedAtUtc);

public record WorkspaceInvitationDto(
    Guid Id,
    Guid WorkspaceId,
    string InvitationCode,
    string? InvitedEmail,
    WorkspaceRole AssignedRole,
    DateTime ExpiresAtUtc);
