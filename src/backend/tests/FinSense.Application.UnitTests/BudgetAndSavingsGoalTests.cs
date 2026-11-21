using FinSense.Application.Features.Budgets.Commands;
using FinSense.Application.Features.Budgets.Queries.GetBudgets;
using FinSense.Application.Features.SavingsGoals.Commands;
using FinSense.Application.Features.SavingsGoals.Queries.GetSavingsGoals;
using FinSense.Application.Features.Transactions.Commands;
using FinSense.Application.UnitTests.Common;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FluentAssertions;

namespace FinSense.Application.UnitTests;

public class BudgetAndSavingsGoalTests
{
    [Fact]
    public async Task GetBudgetsQuery_ShouldComputeUtilizationAndTransitionAcrossNormalWarningExceeded_WhileIgnoringTransfers()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var user = User.Create("budget@finsense.app", "hash", "Budget User", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);

        var bankWallet = Wallet.Create(workspace.Id, "Banka", WalletType.BankAccount, initialBalance: 50000m);
        var savingsWallet = Wallet.Create(workspace.Id, "Birikim", WalletType.Savings, initialBalance: 0m);
        var marketCategory = Category.CreateSystem(workspace.Id, "Market", "Groceries", CategoryType.Expense, "shopping-cart", "#10B981");

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        dbContext.Wallets.AddRange(bankWallet, savingsWallet);
        dbContext.Categories.Add(marketCategory);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);

        var upsertBudgetHandler = new UpsertBudgetCommandHandler(dbContext, accessService, currentUser, dateTimeProvider);
        var getBudgetsHandler = new GetBudgetsQueryHandler(dbContext, accessService, currentUser);
        var createTxHandler = new CreateTransactionCommandHandler(dbContext, accessService, currentUser, dateTimeProvider);

        // Create 10000 TRY budget for Market in Nov 2026 (80% warning threshold)
        await upsertBudgetHandler.Handle(
            new UpsertBudgetCommand(workspace.Id, marketCategory.Id, Year: 2026, Month: 11, LimitAmount: 10000m, AlertThresholdPercent: 80),
            CancellationToken.None);

        // Add a large Transfer of 25000 TRY (must NOT affect budget spentAmount!)
        await createTxHandler.Handle(
            new CreateTransactionCommand(
                workspace.Id, TransactionType.Transfer, bankWallet.Id, savingsWallet.Id, null, null,
                25000m, "TRY", new DateOnly(2026, 11, 5), null, "Birikim transferi", null),
            CancellationToken.None);

        // 1. Spend 5000 TRY -> 50% -> Normal
        await createTxHandler.Handle(
            new CreateTransactionCommand(
                workspace.Id, TransactionType.Expense, bankWallet.Id, null, marketCategory.Id, null,
                5000m, "TRY", new DateOnly(2026, 11, 6), "Migros", null, null),
            CancellationToken.None);

        var normalResult = (await getBudgetsHandler.Handle(new GetBudgetsQuery(workspace.Id, 2026, 11), CancellationToken.None)).Single();
        normalResult.SpentAmount.Should().Be(5000m);
        normalResult.RemainingAmount.Should().Be(5000m);
        normalResult.UtilizationPercent.Should().Be(50m);
        normalResult.Status.Should().Be(BudgetStatus.Normal);

        // 2. Spend +3000 TRY (total 8000) -> 80% -> Warning
        await createTxHandler.Handle(
            new CreateTransactionCommand(
                workspace.Id, TransactionType.Expense, bankWallet.Id, null, marketCategory.Id, null,
                3000m, "TRY", new DateOnly(2026, 11, 8), "CarrefourSA", null, null),
            CancellationToken.None);

        var warningResult = (await getBudgetsHandler.Handle(new GetBudgetsQuery(workspace.Id, 2026, 11), CancellationToken.None)).Single();
        warningResult.SpentAmount.Should().Be(8000m);
        warningResult.RemainingAmount.Should().Be(2000m);
        warningResult.UtilizationPercent.Should().Be(80m);
        warningResult.Status.Should().Be(BudgetStatus.Warning);

        // 3. Spend +2500 TRY (total 10500) -> 105% -> Exceeded
        await createTxHandler.Handle(
            new CreateTransactionCommand(
                workspace.Id, TransactionType.Expense, bankWallet.Id, null, marketCategory.Id, null,
                2500m, "TRY", new DateOnly(2026, 11, 10), "BİM", null, null),
            CancellationToken.None);

        var exceededResult = (await getBudgetsHandler.Handle(new GetBudgetsQuery(workspace.Id, 2026, 11), CancellationToken.None)).Single();
        exceededResult.SpentAmount.Should().Be(10500m);
        exceededResult.RemainingAmount.Should().Be(-500m);
        exceededResult.UtilizationPercent.Should().Be(105m);
        exceededResult.Status.Should().Be(BudgetStatus.Exceeded);
    }

    [Fact]
    public async Task GetSavingsGoalsQuery_WithLinkedWallet_ShouldDynamicallyReflectLinkedWalletBalance()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var user = User.Create("goal@finsense.app", "hash", "Goal User", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);

        var bankWallet = Wallet.Create(workspace.Id, "Banka", WalletType.BankAccount, initialBalance: 30000m);
        var savingsWallet = Wallet.Create(workspace.Id, "Tatil Birikim Hesabı", WalletType.Savings, initialBalance: 10000m);

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        dbContext.Wallets.AddRange(bankWallet, savingsWallet);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);

        var createGoalHandler = new CreateSavingsGoalCommandHandler(dbContext, accessService, dateTimeProvider);
        var getGoalsHandler = new GetSavingsGoalsQueryHandler(dbContext, accessService);
        var createTxHandler = new CreateTransactionCommandHandler(dbContext, accessService, currentUser, dateTimeProvider);

        var createdGoal = await createGoalHandler.Handle(
            new CreateSavingsGoalCommand(
                workspace.Id,
                "Tatil Fonu",
                TargetAmount: 50000m,
                CurrentAmount: 0m,
                LinkedWalletId: savingsWallet.Id,
                TargetDate: new DateOnly(2027, 7, 1)),
            CancellationToken.None);

        createdGoal.CurrentAmount.Should().Be(10000m);
        createdGoal.CompletionPercent.Should().Be(20m);

        // Transfer 15000 TRY into the linked savings wallet
        await createTxHandler.Handle(
            new CreateTransactionCommand(
                workspace.Id, TransactionType.Transfer, bankWallet.Id, savingsWallet.Id, null, null,
                15000m, "TRY", new DateOnly(2026, 11, 10), null, "Tatil birikimi", null),
            CancellationToken.None);

        var goals = await getGoalsHandler.Handle(new GetSavingsGoalsQuery(workspace.Id), CancellationToken.None);
        var goal = goals.Single();

        goal.CurrentAmount.Should().Be(25000m);
        goal.RemainingAmount.Should().Be(25000m);
        goal.CompletionPercent.Should().Be(50m);
    }
}
