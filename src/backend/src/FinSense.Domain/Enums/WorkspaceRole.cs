namespace FinSense.Domain.Enums;

public enum WorkspaceRole : short
{
    Owner = 1,
    Admin = 2,
    Contributor = 3,
    Viewer = 4
}

public static class WorkspaceRoleExtensions
{
    /// <summary>
    /// Checks whether <paramref name="role"/> satisfies at least <paramref name="minimumRole"/>.
    /// Note: Lower numeric enum values represent higher privilege (Owner = 1 ... Viewer = 4).
    /// </summary>
    public static bool HasAtLeast(this WorkspaceRole role, WorkspaceRole minimumRole)
    {
        if (!Enum.IsDefined(role) || !Enum.IsDefined(minimumRole))
        {
            return false;
        }

        return (short)role <= (short)minimumRole;
    }

    public static bool CanViewResources(this WorkspaceRole role) =>
        role.HasAtLeast(WorkspaceRole.Viewer);

    public static bool CanMutateOwnTransactions(this WorkspaceRole role) =>
        role.HasAtLeast(WorkspaceRole.Contributor);

    public static bool CanMutateOtherMembersTransactions(this WorkspaceRole role) =>
        role.HasAtLeast(WorkspaceRole.Admin);

    public static bool CanManageWallets(this WorkspaceRole role) =>
        role.HasAtLeast(WorkspaceRole.Admin);

    public static bool CanManageCategories(this WorkspaceRole role) =>
        role.HasAtLeast(WorkspaceRole.Admin);

    public static bool CanManageBudgetsAndGoals(this WorkspaceRole role) =>
        role.HasAtLeast(WorkspaceRole.Admin);

    public static bool CanManageMembers(this WorkspaceRole role) =>
        role.HasAtLeast(WorkspaceRole.Admin);

    public static bool CanDeleteHousehold(this WorkspaceRole role) =>
        role == WorkspaceRole.Owner;
}
