using FinSense.Domain.Enums;

namespace FinSense.Application.Features.Users.Contracts;

public record UserWorkspaceMembershipDto(
    Guid WorkspaceId,
    string Name,
    WorkspaceType Type,
    string DefaultCurrency,
    WorkspaceRole Role,
    DateTime JoinedAtUtc);

public record UserProfileDto(
    Guid Id,
    string Email,
    string DisplayName,
    string PreferredLocale,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<UserWorkspaceMembershipDto> Workspaces);
