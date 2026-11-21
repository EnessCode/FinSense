using FinSense.Domain.Enums;

namespace FinSense.Application.Common.Interfaces;

public interface IWorkspaceContext
{
    Guid WorkspaceId { get; }
    WorkspaceType WorkspaceType { get; }
    WorkspaceRole Role { get; }
    bool IsInitialized { get; }

    void Initialize(Guid workspaceId, WorkspaceType workspaceType, WorkspaceRole role);
}
