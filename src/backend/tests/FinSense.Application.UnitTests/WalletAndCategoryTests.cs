using FinSense.Application.Common.Exceptions;
using FinSense.Application.Features.Categories.Commands;
using FinSense.Application.Features.Categories.Queries.GetCategories;
using FinSense.Application.Features.Wallets.Commands.CreateWallet;
using FinSense.Application.Features.Wallets.Commands.DeleteWallet;
using FinSense.Application.Features.Wallets.Commands.UpdateWallet;
using FinSense.Application.Features.Wallets.Queries.GetWallets;
using FinSense.Application.UnitTests.Common;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FinSense.Domain.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.UnitTests;

public class WalletAndCategoryTests
{
    [Fact]
    public async Task CreateWallet_ShouldSetCurrentBalanceEqualToInitialBalance()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var user = User.Create("wallet@finsense.app", "hash", "Wallet User", "tr-TR", dateTimeProvider.UtcNow);
        var personalWorkspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(personalWorkspace);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);
        var handler = new CreateWalletCommandHandler(dbContext, accessService, dateTimeProvider);

        var created = await handler.Handle(
            new CreateWalletCommand(
                personalWorkspace.Id,
                "Maaş Hesabı",
                WalletType.BankAccount,
                InitialBalance: 25000.75m,
                CurrencyCode: "TRY"),
            CancellationToken.None);

        created.InitialBalance.Should().Be(25000.75m);
        created.CurrentBalance.Should().Be(25000.75m);
        created.CurrencyCode.Should().Be("TRY");
        created.IsArchived.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateWallet_WhenInitialBalanceChangesInHousehold_ShouldShiftCurrentBalanceByDeltaAndWriteAuditLog()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var owner = User.Create("owner@finsense.app", "hash", "Owner", "tr-TR", dateTimeProvider.UtcNow);
        var household = Workspace.CreateHousehold(owner.Id, "Ev Bütçesi", "TRY", dateTimeProvider.UtcNow);
        var wallet = Wallet.Create(household.Id, "Ortak Kasa", WalletType.Cash, initialBalance: 1000m);
        wallet.ApplyExpense(250m); // CurrentBalance = 750

        dbContext.Users.Add(owner);
        dbContext.Workspaces.Add(household);
        dbContext.Wallets.Add(wallet);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = owner.Id };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);
        var handler = new UpdateWalletCommandHandler(dbContext, accessService, dateTimeProvider);

        var updated = await handler.Handle(
            new UpdateWalletCommand(
                household.Id,
                wallet.Id,
                "Ortak Ev Kasası",
                CreditLimit: null,
                Icon: "wallet",
                ColorHex: "#10B981",
                IsArchived: false,
                InitialBalance: 1500m), // +500 delta
            CancellationToken.None);

        updated.Name.Should().Be("Ortak Ev Kasası");
        updated.InitialBalance.Should().Be(1500m);
        updated.CurrentBalance.Should().Be(1250m);

        var auditLogs = await dbContext.AuditLogs
            .Where(al => al.WorkspaceId == household.Id && al.EntityId == wallet.Id)
            .ToListAsync();

        auditLogs.Should().Contain(al => al.ActionType == "WalletBalanceAdjusted");
        auditLogs.Should().Contain(al => al.ActionType == "WalletUpdated");
    }

    [Fact]
    public async Task SystemCategory_UpdateOrDelete_ShouldThrowDomainException_WhileCustomCategorySoftDeletes()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var user = User.Create("cat@finsense.app", "hash", "Cat User", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);
        var systemCategories = DefaultCategoryCatalog.CreateForWorkspace(workspace.Id);
        var customCategory = Category.CreateCustom(
            workspace.Id, "Evcil Hayvan", "Pets", CategoryType.Expense, "paw", "#F59E0B");

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        dbContext.Categories.AddRange(systemCategories);
        dbContext.Categories.Add(customCategory);
        await dbContext.SaveChangesAsync();

        var systemCategory = systemCategories.First();
        var currentUser = new FakeCurrentUserService { UserId = user.Id };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);

        var updateHandler = new UpdateCategoryCommandHandler(dbContext, accessService, currentUser, dateTimeProvider);
        var deleteHandler = new DeleteCategoryCommandHandler(dbContext, accessService, dateTimeProvider);
        var getHandler = new GetCategoriesQueryHandler(dbContext, accessService, currentUser);

        // 1. System category cannot be updated
        var updateSystemAct = async () => await updateHandler.Handle(
            new UpdateCategoryCommand(workspace.Id, systemCategory.Id, "Değiştirilen", "Changed", "tag", "#000000"),
            CancellationToken.None);

        await updateSystemAct.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "SYSTEM_CATEGORY_IMMUTABLE");

        // 2. System category cannot be deleted
        var deleteSystemAct = async () => await deleteHandler.Handle(
            new DeleteCategoryCommand(workspace.Id, systemCategory.Id),
            CancellationToken.None);

        await deleteSystemAct.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "SYSTEM_CATEGORY_IMMUTABLE");

        // 3. Custom category soft-deletes cleanly and disappears from active list
        await deleteHandler.Handle(
            new DeleteCategoryCommand(workspace.Id, customCategory.Id),
            CancellationToken.None);

        var deletedInDb = await dbContext.Categories.SingleAsync(c => c.Id == customCategory.Id);
        deletedInDb.IsDeleted.Should().BeTrue();

        var activeCategories = await getHandler.Handle(new GetCategoriesQuery(workspace.Id), CancellationToken.None);
        activeCategories.Should().NotContain(c => c.Id == customCategory.Id);
    }

    [Fact]
    public async Task DeleteWallet_ShouldSoftDeleteWallet_DeactivateRecurringRules_UnlinkSavingsGoals_AndPreserveTransactions()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var owner = User.Create("delwallet@finsense.app", "hash", "Owner", "tr-TR", dateTimeProvider.UtcNow);
        var household = Workspace.CreateHousehold(owner.Id, "Ev Bütçesi", "TRY", dateTimeProvider.UtcNow);
        var categories = DefaultCategoryCatalog.CreateForWorkspace(household.Id);
        var expenseCategory = categories.First(c => c.Type == CategoryType.Expense);

        var wallet = Wallet.Create(household.Id, "Silinecek Hesap", WalletType.BankAccount, initialBalance: 5000m);
        var tx = Transaction.Create(
            household.Id,
            wallet.Id,
            TransactionType.Expense,
            400m,
            dateTimeProvider.TodayIstanbul,
            categoryId: expenseCategory.Id,
            createdByUserId: owner.Id,
            utcNow: dateTimeProvider.UtcNow);
        wallet.ApplyExpense(400m); // CurrentBalance = 4600

        var recurring = RecurringTransaction.Create(
            household.Id,
            wallet.Id,
            expenseCategory.Id,
            owner.Id,
            TransactionType.Expense,
            250m,
            "İnternet Faturası",
            dayOfMonth: 15,
            utcNow: dateTimeProvider.UtcNow);

        var linkedGoal = SavingsGoal.Create(
            household.Id,
            "Tatil Fonu",
            targetAmount: 20000m,
            currentAmount: 0m,
            linkedWalletId: wallet.Id);

        dbContext.Users.Add(owner);
        dbContext.Workspaces.Add(household);
        dbContext.Categories.AddRange(categories);
        dbContext.Wallets.Add(wallet);
        dbContext.Transactions.Add(tx);
        dbContext.RecurringTransactions.Add(recurring);
        dbContext.SavingsGoals.Add(linkedGoal);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = owner.Id };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);
        var deleteHandler = new DeleteWalletCommandHandler(dbContext, accessService, dateTimeProvider);
        var getWalletsHandler = new GetWalletsQueryHandler(dbContext, accessService);

        await deleteHandler.Handle(new DeleteWalletCommand(household.Id, wallet.Id), CancellationToken.None);

        var walletInDb = await dbContext.Wallets.SingleAsync(w => w.Id == wallet.Id);
        walletInDb.IsDeleted.Should().BeTrue();
        walletInDb.IsArchived.Should().BeTrue();

        var activeWallets = await getWalletsHandler.Handle(
            new GetWalletsQuery(household.Id, IncludeArchived: true),
            CancellationToken.None);
        activeWallets.Should().NotContain(w => w.Id == wallet.Id);

        var recurringInDb = await dbContext.RecurringTransactions.SingleAsync(r => r.Id == recurring.Id);
        recurringInDb.IsActive.Should().BeFalse();

        var goalInDb = await dbContext.SavingsGoals.SingleAsync(g => g.Id == linkedGoal.Id);
        goalInDb.LinkedWalletId.Should().BeNull();
        goalInDb.CurrentAmount.Should().Be(4600m);

        var txInDb = await dbContext.Transactions.SingleAsync(t => t.Id == tx.Id);
        txInDb.IsDeleted.Should().BeFalse();

        var auditLogs = await dbContext.AuditLogs
            .Where(al => al.WorkspaceId == household.Id && al.EntityId == wallet.Id)
            .ToListAsync();
        auditLogs.Should().Contain(al => al.ActionType == "WalletDeleted");
    }

    [Fact]
    public async Task DeleteWallet_WhenMissingOrUnauthorized_ShouldThrowExpectedExceptions()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var owner = User.Create("owner2@finsense.app", "hash", "Owner", "tr-TR", dateTimeProvider.UtcNow);
        var contributor = User.Create("contrib@finsense.app", "hash", "Contrib", "tr-TR", dateTimeProvider.UtcNow);
        var household = Workspace.CreateHousehold(owner.Id, "Ev", "TRY", dateTimeProvider.UtcNow);
        var contributorMember = WorkspaceMember.Create(
            household.Id,
            contributor.Id,
            WorkspaceRole.Contributor,
            dateTimeProvider.UtcNow);
        var wallet = Wallet.Create(household.Id, "Nakit", WalletType.Cash, initialBalance: 100m);

        dbContext.Users.AddRange(owner, contributor);
        dbContext.Workspaces.Add(household);
        dbContext.WorkspaceMembers.Add(contributorMember);
        dbContext.Wallets.Add(wallet);
        await dbContext.SaveChangesAsync();

        // Contributor cannot delete a wallet (requires Admin)
        var contribUser = new FakeCurrentUserService { UserId = contributor.Id };
        var contribAccess = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, contribUser);
        var contribHandler = new DeleteWalletCommandHandler(dbContext, contribAccess, dateTimeProvider);

        var forbiddenAct = async () => await contribHandler.Handle(
            new DeleteWalletCommand(household.Id, wallet.Id),
            CancellationToken.None);

        await forbiddenAct.Should().ThrowAsync<ForbiddenAccessException>();

        // Owner deleting non-existent wallet gets NotFoundException
        var ownerUser = new FakeCurrentUserService { UserId = owner.Id };
        var ownerAccess = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, ownerUser);
        var ownerHandler = new DeleteWalletCommandHandler(dbContext, ownerAccess, dateTimeProvider);

        var notFoundAct = async () => await ownerHandler.Handle(
            new DeleteWalletCommand(household.Id, Guid.NewGuid()),
            CancellationToken.None);

        await notFoundAct.Should().ThrowAsync<NotFoundException>();
    }
}
