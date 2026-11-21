using FinSense.Application.Common.Interfaces;
using FinSense.Domain.Enums;

namespace FinSense.Api.Services;

public class WorkspaceContext : IWorkspaceContext
{
    public Guid WorkspaceId { get; private set; }
    public WorkspaceType WorkspaceType { get; private set; }
    public WorkspaceRole Role { get; private set; }
    public bool IsInitialized { get; private set; }

    public void Initialize(Guid workspaceId, WorkspaceType workspaceType, WorkspaceRole role)
    {
        WorkspaceId = workspaceId;
        WorkspaceType = workspaceType;
        Role = role;
        IsInitialized = true;
    }
}
