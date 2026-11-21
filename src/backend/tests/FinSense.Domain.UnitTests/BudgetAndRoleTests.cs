using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FluentAssertions;

namespace FinSense.Domain.UnitTests;

public class BudgetAndRoleTests
{
    private readonly Guid _workspaceId = Guid.NewGuid();

    [Theory]
    [InlineData(5000, BudgetStatus.Normal)]
    [InlineData(8000, BudgetStatus.Warning)]
    [InlineData(9500, BudgetStatus.Warning)]
    [InlineData(10000, BudgetStatus.Exceeded)]
    [InlineData(12500, BudgetStatus.Exceeded)]
    public void Budget_EvaluateStatus_ShouldReflectThresholdsAccurately(decimal spentAmount, BudgetStatus expectedStatus)
    {
        var budget = Budget.Create(
            _workspaceId,
            year: 2026,
            month: 11,
            limitAmount: 10000m,
            alertThresholdPercent: 80);

        budget.EvaluateStatus(spentAmount).Should().Be(expectedStatus);
    }

    [Fact]
    public void SavingsGoal_WithLinkedWallet_ShouldDynamicReflectWalletBalanceAndForbidManualOverride()
    {
        var walletId = Guid.NewGuid();
        var goal = SavingsGoal.Create(
            _workspaceId,
            "Tatil Fonu",
            targetAmount: 50000m,
            linkedWalletId: walletId);

        goal.GetEffectiveCurrentAmount(linkedWalletBalance: 18500m).Should().Be(18500m);

        var act = () => goal.Update("Tatil Fonu", 50000m, null, walletId, manualCurrentAmount: 20000m);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == "LINKED_WALLET_GOAL_MANUAL_AMOUNT_FORBIDDEN");
    }

    [Fact]
    public void WorkspaceRole_PermissionHelpers_ShouldEnforceHierarchy()
    {
        WorkspaceRole.Owner.CanDeleteHousehold().Should().BeTrue();
        WorkspaceRole.Admin.CanDeleteHousehold().Should().BeFalse();

        WorkspaceRole.Admin.CanManageWallets().Should().BeTrue();
        WorkspaceRole.Admin.CanManageBudgetsAndGoals().Should().BeTrue();
        WorkspaceRole.Admin.CanMutateOtherMembersTransactions().Should().BeTrue();

        WorkspaceRole.Contributor.CanMutateOwnTransactions().Should().BeTrue();
        WorkspaceRole.Contributor.CanMutateOtherMembersTransactions().Should().BeFalse();
        WorkspaceRole.Contributor.CanManageWallets().Should().BeFalse();

        WorkspaceRole.Viewer.CanViewResources().Should().BeTrue();
        WorkspaceRole.Viewer.CanMutateOwnTransactions().Should().BeFalse();
    }

    [Fact]
    public void PersonalWorkspace_EnsureCanBeDeletedIndependently_ShouldThrowDomainException()
    {
        var personalWorkspace = Workspace.CreatePersonal(Guid.NewGuid());

        var act = () => personalWorkspace.EnsureCanBeDeletedIndependently();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == "PERSONAL_WORKSPACE_CANNOT_BE_DELETED");
    }
}
