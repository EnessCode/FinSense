using FinSense.Application.Features.RecurringTransactions.Commands;
using FinSense.Application.Features.RecurringTransactions.Queries.GetRecurringTransactions;
using FinSense.Application.Features.RecurringTransactions.Services;
using FinSense.Application.UnitTests.Common;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.UnitTests;

public class RecurringTransactionTests
{
    [Fact]
    public async Task CreateRecurringTransaction_WithExecuteFirstImmediately_ShouldDeductWallet_CreateFirstTransaction_AndScheduleNextMonth()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider
        {
            UtcNow = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc)
        };

        var user = User.Create("rec1@finsense.app", "hash", "User", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);
        var wallet = Wallet.Create(workspace.Id, "Maaş Hesabı", WalletType.BankAccount, initialBalance: 5000m);
        var billsCategory = Category.CreateSystem(workspace.Id, "Faturalar", "Bills", CategoryType.Expense, "file-text", "#EF4444");

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        dbContext.Wallets.Add(wallet);
        dbContext.Categories.Add(billsCategory);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id, Locale = "tr-TR" };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);
        var createHandler = new CreateRecurringTransactionCommandHandler(
            dbContext, accessService, currentUser, dateTimeProvider);

        var created = await createHandler.Handle(
            new CreateRecurringTransactionCommand(
                workspace.Id,
                wallet.Id,
                billsCategory.Id,
                TransactionType.Expense,
                Amount: 1000m,
                Title: "Ev İnterneti Sabit Fatura",
                DayOfMonth: 15,
                TotalOccurrences: 12,
                ExecuteFirstImmediately: true),
            CancellationToken.None);

        created.CompletedOccurrences.Should().Be(1);
        created.TotalOccurrences.Should().Be(12);
        created.RemainingOccurrences.Should().Be(11);
        created.IsActive.Should().BeTrue();
        created.NextExecutionDateUtc.Should().Be(new DateTime(2026, 12, 15, 0, 0, 0, DateTimeKind.Utc));

        var updatedWallet = await dbContext.Wallets.AsNoTracking().SingleAsync(w => w.Id == wallet.Id);
        updatedWallet.CurrentBalance.Should().Be(4000m);

        var ledgerTx = await dbContext.Transactions.AsNoTracking().SingleAsync();
        ledgerTx.Amount.Should().Be(1000m);
        ledgerTx.Type.Should().Be(TransactionType.Expense);
        ledgerTx.Notes.Should().Be("Ev İnterneti Sabit Fatura (1/12)");
        ledgerTx.IdempotencyKey.Should().Be($"recurring-{created.Id}-20261111");
    }

    [Fact]
    public async Task ProcessDueTransactionsAsync_ShouldProcessDueInstallment_BeIdempotent_ClampMonthEnd_AndDeactivateAt12Of12()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider
        {
            UtcNow = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc)
        };

        var user = User.Create("rec2@finsense.app", "hash", "User", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);
        var wallet = Wallet.Create(workspace.Id, "Banka", WalletType.BankAccount, initialBalance: 20000m);
        var billsCategory = Category.CreateSystem(workspace.Id, "Faturalar", "Bills", CategoryType.Expense, "file-text", "#EF4444");

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        dbContext.Wallets.Add(wallet);
        dbContext.Categories.Add(billsCategory);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id, Locale = "tr-TR" };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);
        var createHandler = new CreateRecurringTransactionCommandHandler(
            dbContext, accessService, currentUser, dateTimeProvider);
        var processor = new RecurringTransactionProcessor(dbContext, dateTimeProvider);

        // Create 12-month contract billed on the 31st of every month; execute 1/12 immediately
        var created = await createHandler.Handle(
            new CreateRecurringTransactionCommand(
                workspace.Id,
                wallet.Id,
                billsCategory.Id,
                TransactionType.Expense,
                Amount: 1000m,
                Title: "Fiber İnternet",
                DayOfMonth: 31,
                TotalOccurrences: 12,
                ExecuteFirstImmediately: true),
            CancellationToken.None);

        created.NextExecutionDateUtc.Should().Be(new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc));

        // Advance clock to 2026-12-31 -> 2/12 should execute
        dateTimeProvider.UtcNow = new DateTime(2026, 12, 31, 8, 0, 0, DateTimeKind.Utc);
        var secondPassCount = await processor.ProcessDueTransactionsAsync(workspace.Id);
        secondPassCount.Should().Be(1);

        // Running processor again immediately must be a strictly idempotent no-op (0 charges)
        var duplicatePassCount = await processor.ProcessDueTransactionsAsync(workspace.Id);
        duplicatePassCount.Should().Be(0);

        var ruleAfterSecond = await dbContext.RecurringTransactions.AsNoTracking().SingleAsync(r => r.Id == created.Id);
        ruleAfterSecond.CompletedOccurrences.Should().Be(2);
        // Jan 2027 has 31 days
        ruleAfterSecond.NextExecutionDateUtc.Should().Be(new DateTime(2027, 1, 31, 0, 0, 0, DateTimeKind.Utc));

        // Advance to Jan 31 (3/12) and Feb 28 (4/12 - clamped from DayOfMonth=31!) -> March must still target March 31!
        dateTimeProvider.UtcNow = new DateTime(2027, 2, 28, 12, 0, 0, DateTimeKind.Utc);
        var catchUpCount = await processor.ProcessDueTransactionsAsync(workspace.Id);
        catchUpCount.Should().Be(2);

        var ruleAfterFeb = await dbContext.RecurringTransactions.AsNoTracking().SingleAsync(r => r.Id == created.Id);
        ruleAfterFeb.CompletedOccurrences.Should().Be(4);
        ruleAfterFeb.NextExecutionDateUtc.Should().Be(new DateTime(2027, 3, 31, 0, 0, 0, DateTimeKind.Utc));

        // Advance past 12th installment (Oct 31, 2027) -> processes remaining 8 occurrences and deactivates
        dateTimeProvider.UtcNow = new DateTime(2027, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        var finalCatchUp = await processor.ProcessDueTransactionsAsync(workspace.Id);
        finalCatchUp.Should().Be(8);

        var completedRule = await dbContext.RecurringTransactions.AsNoTracking().SingleAsync(r => r.Id == created.Id);
        completedRule.CompletedOccurrences.Should().Be(12);
        completedRule.IsActive.Should().BeFalse();

        // Wallet started at 20000 - 12 * 1000 = 8000
        (await dbContext.Wallets.AsNoTracking().SingleAsync(w => w.Id == wallet.Id))
            .CurrentBalance.Should().Be(8000m);

        (await dbContext.Transactions.CountAsync()).Should().Be(12);

        // Attempting to toggle/reactivate a completed 12/12 rule throws DomainException
        var toggleHandler = new ToggleRecurringTransactionCommandHandler(
            dbContext, accessService, currentUser, dateTimeProvider);
        var reactivateAct = async () => await toggleHandler.Handle(
            new ToggleRecurringTransactionCommand(workspace.Id, created.Id),
            CancellationToken.None);

        await reactivateAct.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "RECURRING_TRANSACTION_COMPLETED");
    }

    [Fact]
    public async Task GetRecurringTransactionsQuery_ShouldComputeMonthlyFixedSummaryAndProcessDueItems()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider
        {
            UtcNow = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc)
        };

        var user = User.Create("rec3@finsense.app", "hash", "User", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);
        var wallet = Wallet.Create(workspace.Id, "Ana Hesap", WalletType.BankAccount, initialBalance: 1000m);
        var salaryCategory = Category.CreateSystem(workspace.Id, "Maaş", "Salary", CategoryType.Income, "wallet", "#22C55E");
        var subCategory = Category.CreateSystem(workspace.Id, "Abonelikler", "Subscriptions", CategoryType.Expense, "repeat", "#6366F1");

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        dbContext.Wallets.Add(wallet);
        dbContext.Categories.AddRange(salaryCategory, subCategory);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id, Locale = "tr-TR" };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);
        var createHandler = new CreateRecurringTransactionCommandHandler(
            dbContext, accessService, currentUser, dateTimeProvider);
        var processor = new RecurringTransactionProcessor(dbContext, dateTimeProvider);
        var queryHandler = new GetRecurringTransactionsQueryHandler(
            dbContext, accessService, currentUser, processor);

        await createHandler.Handle(
            new CreateRecurringTransactionCommand(
                workspace.Id, wallet.Id, salaryCategory.Id, TransactionType.Income,
                50000m, "Maaş", DayOfMonth: 15, TotalOccurrences: null, ExecuteFirstImmediately: false),
            CancellationToken.None);

        await createHandler.Handle(
            new CreateRecurringTransactionCommand(
                workspace.Id, wallet.Id, subCategory.Id, TransactionType.Expense,
                500m, "Netflix", DayOfMonth: 20, TotalOccurrences: null, ExecuteFirstImmediately: false),
            CancellationToken.None);

        // Advance to Nov 16 -> Salary (15th) becomes due and is processed automatically when fetching GET /
        dateTimeProvider.UtcNow = new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);
        var overview = await queryHandler.Handle(new GetRecurringTransactionsQuery(workspace.Id), CancellationToken.None);

        overview.Items.Should().HaveCount(2);
        overview.TotalMonthlyFixedIncome.Should().Be(50000m);
        overview.TotalMonthlyFixedExpense.Should().Be(500m);
        overview.NetMonthlyFixedFlow.Should().Be(49500m);

        (await dbContext.Wallets.AsNoTracking().SingleAsync(w => w.Id == wallet.Id))
            .CurrentBalance.Should().Be(51000m);
    }
}
