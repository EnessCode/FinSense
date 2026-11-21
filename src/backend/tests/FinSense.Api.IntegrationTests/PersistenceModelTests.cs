using FinSense.Domain.Entities;
using FinSense.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Api.IntegrationTests;

public class PersistenceModelTests
{
    private static FinSenseDbContext CreateDesignTimeContext()
    {
        var factory = new FinSenseDbContextFactory();
        return factory.CreateDbContext([]);
    }

    [Fact]
    public void Model_ShouldMapAllEntitiesToExpectedSnakeCaseTableNames()
    {
        using var context = CreateDesignTimeContext();
        var model = context.Model;

        model.FindEntityType(typeof(User))!.GetTableName().Should().Be("users");
        model.FindEntityType(typeof(RefreshToken))!.GetTableName().Should().Be("refresh_tokens");
        model.FindEntityType(typeof(Workspace))!.GetTableName().Should().Be("workspaces");
        model.FindEntityType(typeof(WorkspaceMember))!.GetTableName().Should().Be("workspace_members");
        model.FindEntityType(typeof(WorkspaceInvitation))!.GetTableName().Should().Be("workspace_invitations");
        model.FindEntityType(typeof(Wallet))!.GetTableName().Should().Be("wallets");
        model.FindEntityType(typeof(Category))!.GetTableName().Should().Be("categories");
        model.FindEntityType(typeof(Transaction))!.GetTableName().Should().Be("transactions");
        model.FindEntityType(typeof(Budget))!.GetTableName().Should().Be("budgets");
        model.FindEntityType(typeof(SavingsGoal))!.GetTableName().Should().Be("savings_goals");
        model.FindEntityType(typeof(Receipt))!.GetTableName().Should().Be("receipts");
        model.FindEntityType(typeof(AiConversation))!.GetTableName().Should().Be("ai_conversations");
        model.FindEntityType(typeof(AiMessage))!.GetTableName().Should().Be("ai_messages");
        model.FindEntityType(typeof(AuditLog))!.GetTableName().Should().Be("audit_logs");
    }

    [Fact]
    public void Model_ShouldConfigureXminConcurrencyTokenOnWalletAndBudget()
    {
        using var context = CreateDesignTimeContext();

        var walletXmin = context.Model.FindEntityType(typeof(Wallet))!.FindProperty("xmin");
        walletXmin.Should().NotBeNull();
        walletXmin!.IsConcurrencyToken.Should().BeTrue();

        var budgetXmin = context.Model.FindEntityType(typeof(Budget))!.FindProperty("xmin");
        budgetXmin.Should().NotBeNull();
        budgetXmin!.IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public void Model_ShouldConfigureAllMandatoryCompositeAndFilteredIndexes()
    {
        using var context = CreateDesignTimeContext();

        var transactionIndexes = context.Model.FindEntityType(typeof(Transaction))!.GetIndexes().ToList();
        transactionIndexes.Should().Contain(i =>
            i.GetDatabaseName() == "IX_transactions_workspace_date_type" &&
            i.GetFilter() == "is_deleted = FALSE");

        transactionIndexes.Should().Contain(i =>
            i.GetDatabaseName() == "IX_transactions_workspace_category_date" &&
            i.GetFilter() == "is_deleted = FALSE AND type = 2");

        transactionIndexes.Should().Contain(i =>
            i.GetDatabaseName() == "IX_transactions_workspace_idempotency" &&
            i.IsUnique &&
            i.GetFilter() == "idempotency_key IS NOT NULL");

        var memberIndexes = context.Model.FindEntityType(typeof(WorkspaceMember))!.GetIndexes().ToList();
        memberIndexes.Should().Contain(i =>
            i.GetDatabaseName() == "IX_workspace_members_workspace_user" &&
            i.IsUnique);

        var budgetIndexes = context.Model.FindEntityType(typeof(Budget))!.GetIndexes().ToList();
        budgetIndexes.Should().Contain(i =>
            i.GetDatabaseName() == "IX_budgets_workspace_year_month_category" &&
            i.IsUnique &&
            i.GetFilter() == "is_deleted = FALSE");
    }
}
