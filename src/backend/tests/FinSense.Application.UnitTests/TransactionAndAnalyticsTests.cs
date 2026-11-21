using FinSense.Application.Common.Exceptions;
using FinSense.Application.Features.Analytics.Queries.GetAnalyticsSummary;
using FinSense.Application.Features.Transactions.Commands;
using FinSense.Application.UnitTests.Common;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.UnitTests;

public class TransactionAndAnalyticsTests
{
    [Fact]
    public async Task CreditCardExpense_FollowedByBankTransferSettlement_ShouldReconcileBalancesWithoutDoubleCountingAnalytics()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var user = User.Create("ledger@finsense.app", "hash", "Ledger User", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);

        var bankAccount = Wallet.Create(workspace.Id, "Vadesiz Hesap", WalletType.BankAccount, initialBalance: 10000m);
        var creditCard = Wallet.Create(workspace.Id, "Kredi Kartı", WalletType.CreditCard, initialBalance: 0m, creditLimit: 50000m);
        var marketCategory = Category.CreateSystem(workspace.Id, "Market", "Groceries", CategoryType.Expense, "shopping-cart", "#10B981");

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        dbContext.Wallets.AddRange(bankAccount, creditCard);
        dbContext.Categories.Add(marketCategory);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);
        var createTxHandler = new CreateTransactionCommandHandler(dbContext, accessService, currentUser, dateTimeProvider);
        var analyticsHandler = new GetAnalyticsSummaryQueryHandler(dbContext, accessService, currentUser);

        var txDate = new DateOnly(2026, 11, 10);

        // 1. Log 1500 TRY Expense on CreditCard
        await createTxHandler.Handle(
            new CreateTransactionCommand(
                workspace.Id,
                TransactionType.Expense,
                creditCard.Id,
                CounterpartyWalletId: null,
                CategoryId: marketCategory.Id,
                ReceiptId: null,
                Amount: 1500m,
                CurrencyCode: "TRY",
                TransactionDate: txDate,
                MerchantName: "Migros",
                Notes: null,
                PaidByUserId: null),
            CancellationToken.None);

        (await dbContext.Wallets.AsNoTracking().SingleAsync(w => w.Id == creditCard.Id))
            .CurrentBalance.Should().Be(-1500m);

        // 2. Log 1500 TRY Transfer from BankAccount to CreditCard (paying off card bill)
        await createTxHandler.Handle(
            new CreateTransactionCommand(
                workspace.Id,
                TransactionType.Transfer,
                bankAccount.Id,
                CounterpartyWalletId: creditCard.Id,
                CategoryId: null,
                ReceiptId: null,
                Amount: 1500m,
                CurrencyCode: "TRY",
                TransactionDate: txDate,
                MerchantName: null,
                Notes: "Kredi kartı borç ödemesi",
                PaidByUserId: null),
            CancellationToken.None);

        var updatedBank = await dbContext.Wallets.AsNoTracking().SingleAsync(w => w.Id == bankAccount.Id);
        var updatedCard = await dbContext.Wallets.AsNoTracking().SingleAsync(w => w.Id == creditCard.Id);
        updatedBank.CurrentBalance.Should().Be(8500m);
        updatedCard.CurrentBalance.Should().Be(0m);

        // 3. Analytics must report ONLY 1500 TRY expense (no double-counting from Transfer)
        var summary = await analyticsHandler.Handle(
            new GetAnalyticsSummaryQuery(workspace.Id, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)),
            CancellationToken.None);

        summary.TotalExpense.Should().Be(1500m);
        summary.TotalIncome.Should().Be(0m);
        summary.NetCashFlow.Should().Be(-1500m);
        summary.CategoryBreakdown.Should().ContainSingle(c => c.CategoryId == marketCategory.Id && c.Amount == 1500m);
    }

    [Fact]
    public async Task CreateTransaction_WithDuplicateIdempotencyKey_ShouldReturnExistingTransactionWithoutDoubleDeduction()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var user = User.Create("idem@finsense.app", "hash", "Idem User", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);
        var wallet = Wallet.Create(workspace.Id, "Nakit", WalletType.Cash, initialBalance: 2000m);
        var category = Category.CreateSystem(workspace.Id, "Market", "Groceries", CategoryType.Expense, "shopping-cart", "#10B981");

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        dbContext.Wallets.Add(wallet);
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);
        var handler = new CreateTransactionCommandHandler(dbContext, accessService, currentUser, dateTimeProvider);

        var command = new CreateTransactionCommand(
            workspace.Id,
            TransactionType.Expense,
            wallet.Id,
            null,
            category.Id,
            null,
            450m,
            "TRY",
            new DateOnly(2026, 11, 10),
            "BİM",
            null,
            null,
            IdempotencyKey: "req-idem-key-001");

        var firstResult = await handler.Handle(command, CancellationToken.None);
        var secondResult = await handler.Handle(command, CancellationToken.None);

        secondResult.Id.Should().Be(firstResult.Id);
        (await dbContext.Transactions.CountAsync()).Should().Be(1);
        (await dbContext.Wallets.AsNoTracking().SingleAsync(w => w.Id == wallet.Id))
            .CurrentBalance.Should().Be(1550m);
    }

    [Fact]
    public async Task UpdateAndDeleteTransaction_ShouldAccuratelyReconcileWalletBalances()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var user = User.Create("reconcile@finsense.app", "hash", "User", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);
        var wallet = Wallet.Create(workspace.Id, "Banka", WalletType.BankAccount, initialBalance: 5000m);
        var category = Category.CreateSystem(workspace.Id, "Faturalar", "Bills", CategoryType.Expense, "file-text", "#EF4444");

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        dbContext.Wallets.Add(wallet);
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);
        var createHandler = new CreateTransactionCommandHandler(dbContext, accessService, currentUser, dateTimeProvider);
        var updateHandler = new UpdateTransactionCommandHandler(dbContext, accessService, currentUser, dateTimeProvider);
        var deleteHandler = new DeleteTransactionCommandHandler(dbContext, accessService, dateTimeProvider);

        var created = await createHandler.Handle(
            new CreateTransactionCommand(
                workspace.Id, TransactionType.Expense, wallet.Id, null, category.Id, null,
                500m, "TRY", new DateOnly(2026, 11, 10), "Elektrik", null, null),
            CancellationToken.None);

        (await dbContext.Wallets.AsNoTracking().SingleAsync(w => w.Id == wallet.Id))
            .CurrentBalance.Should().Be(4500m);

        // Update expense from 500 -> 800
        await updateHandler.Handle(
            new UpdateTransactionCommand(
                workspace.Id, created.Id, TransactionType.Expense, wallet.Id, null, category.Id,
                800m, new DateOnly(2026, 11, 10), "Elektrik Faturası", null, null),
            CancellationToken.None);

        (await dbContext.Wallets.AsNoTracking().SingleAsync(w => w.Id == wallet.Id))
            .CurrentBalance.Should().Be(4200m);

        // Soft-delete transaction -> reverses 800 back to 5000
        await deleteHandler.Handle(new DeleteTransactionCommand(workspace.Id, created.Id), CancellationToken.None);

        var finalWallet = await dbContext.Wallets.AsNoTracking().SingleAsync(w => w.Id == wallet.Id);
        var deletedTx = await dbContext.Transactions.AsNoTracking().SingleAsync(t => t.Id == created.Id);

        finalWallet.CurrentBalance.Should().Be(5000m);
        deletedTx.IsDeleted.Should().BeTrue();
        deletedTx.DeletedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task HouseholdTransactionPermissions_ContributorCannotMutateOthersTransaction_WhileAdminCan_AndForeignUserIsDenied()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();

        var owner = User.Create("owner@finsense.app", "hash", "Owner", "tr-TR", dateTimeProvider.UtcNow);
        var admin = User.Create("admin@finsense.app", "hash", "Admin", "tr-TR", dateTimeProvider.UtcNow);
        var contributor = User.Create("contrib@finsense.app", "hash", "Contributor", "tr-TR", dateTimeProvider.UtcNow);
        var outsider = User.Create("outsider@finsense.app", "hash", "Outsider", "tr-TR", dateTimeProvider.UtcNow);

        var household = Workspace.CreateHousehold(owner.Id, "Ev Bütçesi", "TRY", dateTimeProvider.UtcNow);
        var adminMember = WorkspaceMember.Create(household.Id, admin.Id, WorkspaceRole.Admin, dateTimeProvider.UtcNow);
        var contributorMember = WorkspaceMember.Create(household.Id, contributor.Id, WorkspaceRole.Contributor, dateTimeProvider.UtcNow);

        var wallet = Wallet.Create(household.Id, "Ortak Hesap", WalletType.BankAccount, initialBalance: 10000m);
        var category = Category.CreateSystem(household.Id, "Market", "Groceries", CategoryType.Expense, "shopping-cart", "#10B981");

        dbContext.Users.AddRange(owner, admin, contributor, outsider);
        dbContext.Workspaces.Add(household);
        dbContext.WorkspaceMembers.AddRange(adminMember, contributorMember);
        dbContext.Wallets.Add(wallet);
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync();

        // Owner creates a transaction
        var ownerUser = new FakeCurrentUserService { UserId = owner.Id };
        var createByOwner = new CreateTransactionCommandHandler(
            dbContext,
            TestApplicationHarness.CreateWorkspaceAccessService(dbContext, ownerUser),
            ownerUser,
            dateTimeProvider);

        var ownerTx = await createByOwner.Handle(
            new CreateTransactionCommand(
                household.Id, TransactionType.Expense, wallet.Id, null, category.Id, null,
                300m, "TRY", new DateOnly(2026, 11, 10), "Fırın", null, null),
            CancellationToken.None);

        // 1. Contributor attempting to edit or delete Owner's transaction -> ForbiddenAccessException
        var contribUser = new FakeCurrentUserService { UserId = contributor.Id };
        var contribAccess = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, contribUser);
        var updateByContrib = new UpdateTransactionCommandHandler(dbContext, contribAccess, contribUser, dateTimeProvider);
        var deleteByContrib = new DeleteTransactionCommandHandler(dbContext, contribAccess, dateTimeProvider);

        var contribEditAct = async () => await updateByContrib.Handle(
            new UpdateTransactionCommand(
                household.Id, ownerTx.Id, TransactionType.Expense, wallet.Id, null, category.Id,
                350m, new DateOnly(2026, 11, 10), "Fırın", null, null),
            CancellationToken.None);

        var contribDeleteAct = async () => await deleteByContrib.Handle(
            new DeleteTransactionCommand(household.Id, ownerTx.Id),
            CancellationToken.None);

        await contribEditAct.Should().ThrowAsync<ForbiddenAccessException>()
            .Where(e => e.ErrorCode == "TRANSACTION_EDIT_FORBIDDEN");
        await contribDeleteAct.Should().ThrowAsync<ForbiddenAccessException>()
            .Where(e => e.ErrorCode == "TRANSACTION_DELETE_FORBIDDEN");

        // 2. Outsider attempting to query workspace analytics -> ForbiddenAccessException (WORKSPACE_ACCESS_DENIED)
        var outsiderUser = new FakeCurrentUserService { UserId = outsider.Id };
        var outsiderAnalytics = new GetAnalyticsSummaryQueryHandler(
            dbContext,
            TestApplicationHarness.CreateWorkspaceAccessService(dbContext, outsiderUser),
            outsiderUser);

        var outsiderAct = async () => await outsiderAnalytics.Handle(
            new GetAnalyticsSummaryQuery(household.Id, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)),
            CancellationToken.None);

        await outsiderAct.Should().ThrowAsync<ForbiddenAccessException>()
            .Where(e => e.ErrorCode == "WORKSPACE_ACCESS_DENIED");

        // 3. Admin CAN edit and delete Owner's transaction
        var adminUser = new FakeCurrentUserService { UserId = admin.Id };
        var adminAccess = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, adminUser);
        var updateByAdmin = new UpdateTransactionCommandHandler(dbContext, adminAccess, adminUser, dateTimeProvider);
        var deleteByAdmin = new DeleteTransactionCommandHandler(dbContext, adminAccess, dateTimeProvider);

        var updated = await updateByAdmin.Handle(
            new UpdateTransactionCommand(
                household.Id, ownerTx.Id, TransactionType.Expense, wallet.Id, null, category.Id,
                400m, new DateOnly(2026, 11, 10), "Fırın Güncel", null, null),
            CancellationToken.None);

        updated.Amount.Should().Be(400m);

        await deleteByAdmin.Handle(new DeleteTransactionCommand(household.Id, ownerTx.Id), CancellationToken.None);

        (await dbContext.Wallets.AsNoTracking().SingleAsync(w => w.Id == wallet.Id))
            .CurrentBalance.Should().Be(10000m);
    }

    [Fact]
    public async Task Transfer_ExceedingSourceWalletBalance_ShouldThrowDomainException_WhileExpenseRemainsAllowed()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var user = User.Create("transfer-guard@finsense.app", "hash", "Transfer User", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);

        var sourceWallet = Wallet.Create(workspace.Id, "Vadesiz Hesap", WalletType.BankAccount, initialBalance: 500m);
        var targetWallet = Wallet.Create(workspace.Id, "Birikim Hesabı", WalletType.Savings, initialBalance: 100m);
        var expenseCategory = Category.CreateSystem(workspace.Id, "Market", "Groceries", CategoryType.Expense, "shopping-cart", "#10B981");

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        dbContext.Wallets.AddRange(sourceWallet, targetWallet);
        dbContext.Categories.Add(expenseCategory);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);
        var createHandler = new CreateTransactionCommandHandler(dbContext, accessService, currentUser, dateTimeProvider);
        var updateHandler = new UpdateTransactionCommandHandler(dbContext, accessService, currentUser, dateTimeProvider);

        // 1. Attempting a Transfer of 600 TRY from a 500 TRY wallet must fail
        var createTransferAct = async () => await createHandler.Handle(
            new CreateTransactionCommand(
                workspace.Id,
                TransactionType.Transfer,
                sourceWallet.Id,
                CounterpartyWalletId: targetWallet.Id,
                CategoryId: null,
                ReceiptId: null,
                Amount: 600m,
                CurrencyCode: "TRY",
                TransactionDate: new DateOnly(2026, 11, 11),
                MerchantName: null,
                Notes: "Yetersiz bakiye transfer denemesi",
                PaidByUserId: null),
            CancellationToken.None);

        await createTransferAct.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INSUFFICIENT_WALLET_BALANCE_FOR_TRANSFER" &&
                        e.Message == "Transfer tutarı kaynak cüzdanın mevcut bakiyesini aşamaz.");

        (await dbContext.Wallets.AsNoTracking().SingleAsync(w => w.Id == sourceWallet.Id))
            .CurrentBalance.Should().Be(500m);

        // 2. Updating a valid 300 TRY transfer to 600 TRY must also fail without altering balances
        var validTransfer = await createHandler.Handle(
            new CreateTransactionCommand(
                workspace.Id,
                TransactionType.Transfer,
                sourceWallet.Id,
                CounterpartyWalletId: targetWallet.Id,
                CategoryId: null,
                ReceiptId: null,
                Amount: 300m,
                CurrencyCode: "TRY",
                TransactionDate: new DateOnly(2026, 11, 11),
                MerchantName: null,
                Notes: "Geçerli transfer",
                PaidByUserId: null),
            CancellationToken.None);

        var updateTransferAct = async () => await updateHandler.Handle(
            new UpdateTransactionCommand(
                workspace.Id,
                validTransfer.Id,
                TransactionType.Transfer,
                sourceWallet.Id,
                CounterpartyWalletId: targetWallet.Id,
                CategoryId: null,
                Amount: 600m,
                TransactionDate: new DateOnly(2026, 11, 11),
                MerchantName: null,
                Notes: "Güncelleme limiti aşımı",
                PaidByUserId: null),
            CancellationToken.None);

        await updateTransferAct.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INSUFFICIENT_WALLET_BALANCE_FOR_TRANSFER");

        // 3. Normal Expense exceeding current balance (200 TRY remaining -> 600 TRY expense) remains allowed
        await createHandler.Handle(
            new CreateTransactionCommand(
                workspace.Id,
                TransactionType.Expense,
                sourceWallet.Id,
                CounterpartyWalletId: null,
                CategoryId: expenseCategory.Id,
                ReceiptId: null,
                Amount: 600m,
                CurrencyCode: "TRY",
                TransactionDate: new DateOnly(2026, 11, 11),
                MerchantName: "Market",
                Notes: null,
                PaidByUserId: null),
            CancellationToken.None);

        (await dbContext.Wallets.AsNoTracking().SingleAsync(w => w.Id == sourceWallet.Id))
            .CurrentBalance.Should().Be(-400m);
    }

    [Fact]
    public async Task CreateExpenseWithInstallments_ShouldSplitAccuratelyWithRemainderOnFirst_AdvanceDates_AndFilterMonthlyAnalytics()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var user = User.Create("installment@finsense.app", "hash", "Installment User", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);

        var creditCard = Wallet.Create(
            workspace.Id,
            "Bonus Kart",
            WalletType.CreditCard,
            initialBalance: 0m,
            creditLimit: 50000m);
        var techCategory = Category.CreateSystem(workspace.Id, "Teknoloji", "Technology", CategoryType.Expense, "cpu", "#3B82F6");

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        dbContext.Wallets.Add(creditCard);
        dbContext.Categories.Add(techCategory);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);
        var createHandler = new CreateTransactionCommandHandler(dbContext, accessService, currentUser, dateTimeProvider);
        var analyticsHandler = new GetAnalyticsSummaryQueryHandler(dbContext, accessService, currentUser);

        // 1000 TRY split across 3 installments -> 333.34 + 333.33 + 333.33 = 1000.00
        var firstDetail = await createHandler.Handle(
            new CreateTransactionCommand(
                workspace.Id,
                TransactionType.Expense,
                creditCard.Id,
                CounterpartyWalletId: null,
                CategoryId: techCategory.Id,
                ReceiptId: null,
                Amount: 1000m,
                CurrencyCode: "TRY",
                TransactionDate: new DateOnly(2026, 11, 20),
                MerchantName: "Teknosa",
                Notes: "Kulaklık",
                PaidByUserId: null,
                IdempotencyKey: "inst-key-1",
                InstallmentCount: 3),
            CancellationToken.None);

        firstDetail.Amount.Should().Be(333.34m);
        firstDetail.TransactionDate.Should().Be(new DateOnly(2026, 11, 20));
        firstDetail.MerchantName.Should().Be("Teknosa (Taksit 1/3)");
        firstDetail.Notes.Should().Be("Kulaklık (Taksit 1/3)");

        var allTx = await dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.WorkspaceId == workspace.Id && !t.IsDeleted)
            .OrderBy(t => t.TransactionDate)
            .ToListAsync();

        allTx.Should().HaveCount(3);
        allTx.Select(t => t.Amount).Should().Equal(333.34m, 333.33m, 333.33m);
        allTx.Sum(t => t.Amount).Should().Be(1000m);
        allTx.Select(t => t.TransactionDate).Should().Equal(
            new DateOnly(2026, 11, 20),
            new DateOnly(2026, 12, 20),
            new DateOnly(2027, 1, 20));
        allTx.Select(t => t.Notes).Should().Equal(
            "Kulaklık (Taksit 1/3)",
            "Kulaklık (Taksit 2/3)",
            "Kulaklık (Taksit 3/3)");

        // Wallet reflects full purchase liability immediately (-1000 TRY)
        (await dbContext.Wallets.AsNoTracking().SingleAsync(w => w.Id == creditCard.Id))
            .CurrentBalance.Should().Be(-1000m);

        // Month-filtered analytics show only that month's installment
        var novSummary = await analyticsHandler.Handle(
            new GetAnalyticsSummaryQuery(workspace.Id, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)),
            CancellationToken.None);
        var decSummary = await analyticsHandler.Handle(
            new GetAnalyticsSummaryQuery(workspace.Id, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31)),
            CancellationToken.None);
        var janSummary = await analyticsHandler.Handle(
            new GetAnalyticsSummaryQuery(workspace.Id, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31)),
            CancellationToken.None);

        novSummary.TotalExpense.Should().Be(333.34m);
        decSummary.TotalExpense.Should().Be(333.33m);
        janSummary.TotalExpense.Should().Be(333.33m);
    }

    [Fact]
    public async Task CreateTransaction_WithInvalidInstallmentCountOrNonExpense_ShouldThrowDomainException()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var user = User.Create("inst-guard@finsense.app", "hash", "Guard User", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);
        var wallet = Wallet.Create(workspace.Id, "Vadesiz", WalletType.BankAccount, initialBalance: 5000m);
        var incomeCategory = Category.CreateSystem(workspace.Id, "Maaş", "Salary", CategoryType.Income, "briefcase", "#10B981");
        var expenseCategory = Category.CreateSystem(workspace.Id, "Market", "Groceries", CategoryType.Expense, "shopping-cart", "#10B981");

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        dbContext.Wallets.Add(wallet);
        dbContext.Categories.AddRange(incomeCategory, expenseCategory);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);
        var createHandler = new CreateTransactionCommandHandler(dbContext, accessService, currentUser, dateTimeProvider);

        var incomeInstallmentAct = async () => await createHandler.Handle(
            new CreateTransactionCommand(
                workspace.Id,
                TransactionType.Income,
                wallet.Id,
                null,
                incomeCategory.Id,
                null,
                3000m,
                "TRY",
                new DateOnly(2026, 11, 20),
                null,
                null,
                null,
                InstallmentCount: 3),
            CancellationToken.None);

        await incomeInstallmentAct.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INSTALLMENT_EXPENSE_ONLY");

        var outOfRangeAct = async () => await createHandler.Handle(
            new CreateTransactionCommand(
                workspace.Id,
                TransactionType.Expense,
                wallet.Id,
                null,
                expenseCategory.Id,
                null,
                3000m,
                "TRY",
                new DateOnly(2026, 11, 20),
                null,
                null,
                null,
                InstallmentCount: 25),
            CancellationToken.None);

        await outOfRangeAct.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_INSTALLMENT_COUNT");
    }
}
