using FinSense.Application.Common.Interfaces;
using FinSense.Application.Features.Ai.Commands.AskFinancialAssistant;
using FinSense.Application.Features.Ai.Commands.ClearAiChatHistory;
using FinSense.Application.Features.Ai.Commands.ScanReceipt;
using FinSense.Application.Features.Ai.Queries.GetAiChatHistory;
using FinSense.Application.UnitTests.Common;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FinSense.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinSense.Application.UnitTests;

public class AiIntegrationTests
{
    private sealed class FakeAiServiceClient : IAiServiceClient
    {
        public InternalAiChatRequest? LastChatRequest { get; private set; }
        public InternalReceiptOcrRequest? LastReceiptRequest { get; private set; }

        public InternalAiChatResponse ChatResponseToReturn { get; set; } = new(
            IsFinanceRelated: true,
            Intent: "SPENDING_QUERY",
            Answer: "Bu ay Market kategorisinde 1.500,00 TL harcama yaptınız.",
            HighlightedCategory: "Market",
            SuggestedAction: "Market harcamalarınızı bütçe sınırında tutmaya devam edin.",
            ProviderUsed: "smart-local");

        public InternalReceiptOcrResponse ReceiptResponseToReturn { get; set; } = new(
            IsValidReceipt: true,
            RejectionReason: null,
            MerchantName: "MİGROS TİCARET A.Ş.",
            TransactionDate: "2026-11-11",
            TotalAmount: 425.50m,
            TaxAmount: 38.68m,
            Currency: "TRY",
            SuggestedCategory: "Market",
            Confidence: 0.94,
            ProviderUsed: "smart-local");

        public Task<InternalAiChatResponse> ChatAsync(
            InternalAiChatRequest request,
            CancellationToken cancellationToken = default)
        {
            LastChatRequest = request;
            return Task.FromResult(ChatResponseToReturn);
        }

        public Task<InternalReceiptOcrResponse> ExtractReceiptAsync(
            InternalReceiptOcrRequest request,
            CancellationToken cancellationToken = default)
        {
            LastReceiptRequest = request;
            return Task.FromResult(ReceiptResponseToReturn);
        }
    }

    private static InMemoryAiQuotaService CreateQuotaService(
        IDateTimeProvider dateTimeProvider,
        int dailyChatLimit = 15,
        int dailyReceiptLimit = 10)
    {
        return new InMemoryAiQuotaService(
            dateTimeProvider,
            Options.Create(new AiOptions
            {
                DailyChatQuotaPerUser = dailyChatLimit,
                DailyReceiptQuotaPerUser = dailyReceiptLimit
            }));
    }

    [Fact]
    public async Task AskFinancialAssistantCommand_ShouldAggregateDeterministicContext_ExcludeTransfers_AndDecrementQuota()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider
        {
            UtcNow = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc)
        };

        var owner = User.Create("ai-owner@finsense.app", "hash", "Enes", "tr-TR", dateTimeProvider.UtcNow);
        var partner = User.Create("ai-partner@finsense.app", "hash", "Ayşe", "tr-TR", dateTimeProvider.UtcNow);
        var household = Workspace.CreateHousehold(owner.Id, "Ev Bütçesi", "TRY", dateTimeProvider.UtcNow);
        var partnerMember = WorkspaceMember.Create(household.Id, partner.Id, WorkspaceRole.Admin, dateTimeProvider.UtcNow);

        var bankWallet = Wallet.Create(household.Id, "Banka", WalletType.BankAccount, initialBalance: 12000m);
        var savingsWallet = Wallet.Create(household.Id, "Birikim", WalletType.Savings, initialBalance: 5000m);
        var archivedWallet = Wallet.Create(household.Id, "Eski Kart", WalletType.Cash, initialBalance: 9999m);
        archivedWallet.Archive();

        var salaryCategory = Category.CreateSystem(household.Id, "Maaş", "Salary", CategoryType.Income, "wallet", "#22C55E");
        var marketCategory = Category.CreateSystem(household.Id, "Market", "Groceries", CategoryType.Expense, "shopping-cart", "#10B981");
        var marketBudget = Budget.Create(household.Id, 2026, 11, limitAmount: 2000m, categoryId: marketCategory.Id);

        var incomeTx = Transaction.Create(
            household.Id, bankWallet.Id, TransactionType.Income, 30000m, new DateOnly(2026, 11, 2),
            categoryId: salaryCategory.Id, createdByUserId: owner.Id, paidByUserId: owner.Id);

        var marketExpense = Transaction.Create(
            household.Id, bankWallet.Id, TransactionType.Expense, 1500m, new DateOnly(2026, 11, 10),
            categoryId: marketCategory.Id, merchantName: "Migros", createdByUserId: partner.Id, paidByUserId: partner.Id);

        // Inter-wallet Transfer: MUST be excluded from monthly_expense & monthly_income
        var transferTx = Transaction.Create(
            household.Id, bankWallet.Id, TransactionType.Transfer, 4000m, new DateOnly(2026, 11, 11),
            counterpartyWalletId: savingsWallet.Id, createdByUserId: owner.Id, paidByUserId: owner.Id);

        dbContext.Users.AddRange(owner, partner);
        dbContext.Workspaces.Add(household);
        dbContext.WorkspaceMembers.Add(partnerMember);
        dbContext.Wallets.AddRange(bankWallet, savingsWallet, archivedWallet);
        dbContext.Categories.AddRange(salaryCategory, marketCategory);
        dbContext.Budgets.Add(marketBudget);
        dbContext.Transactions.AddRange(incomeTx, marketExpense, transferTx);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = owner.Id, Locale = "tr-TR" };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);
        var quotaService = CreateQuotaService(dateTimeProvider, dailyChatLimit: 15);
        var fakeAiClient = new FakeAiServiceClient();

        var handler = new AskFinancialAssistantCommandHandler(
            dbContext,
            accessService,
            currentUser,
            dateTimeProvider,
            quotaService,
            fakeAiClient);

        var response = await handler.Handle(
            new AskFinancialAssistantCommand(household.Id, "Bu ay market harcamalarım ne durumda?"),
            CancellationToken.None);

        response.IsFinanceRelated.Should().BeTrue();
        response.RemainingDailyQuota.Should().Be(14);
        response.ProviderUsed.Should().Be("smart-local");

        fakeAiClient.LastChatRequest.Should().NotBeNull();
        fakeAiClient.LastChatRequest!.WorkspaceType.Should().Be("Household");
        fakeAiClient.LastChatRequest.Locale.Should().Be("tr-TR");

        var ctx = fakeAiClient.LastChatRequest.FinancialContext;
        // Total balance excludes archivedWallet (9999)
        ctx.TotalBalance.Should().Be(17000m);
        ctx.MonthlyIncome.Should().Be(30000m);
        // Transfer (4000) is excluded; only Market expense (1500) counts
        ctx.MonthlyExpense.Should().Be(1500m);
        ctx.NetCashFlow.Should().Be(28500m);
        ctx.SavingsRatePercent.Should().Be(95.00m);

        ctx.TopCategories.Should().ContainSingle(c =>
            c.CategoryName == "Market" && c.Amount == 1500m && c.Percentage == 100m && c.TransactionCount == 1);

        ctx.BudgetAlerts.Should().ContainSingle(b =>
            b.CategoryName == "Market" &&
            b.LimitAmount == 2000m &&
            b.SpentAmount == 1500m &&
            b.UtilizationPercent == 75m &&
            b.Status == "Normal");

        ctx.MemberBreakdown.Should().ContainSingle(m =>
            m.MemberName == "Ayşe" && m.Amount == 1500m && m.Percentage == 100m);
    }

    [Fact]
    public async Task ScanReceiptCommand_ShouldMapSuggestedCategoryNameToWorkspaceCategoryId()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var user = User.Create("ocr@finsense.app", "hash", "OCR User", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);

        var marketCategory = Category.CreateSystem(workspace.Id, "Market", "Groceries", CategoryType.Expense, "shopping-cart", "#10B981");
        var diningCategory = Category.CreateSystem(workspace.Id, "Yeme-İçme", "Dining", CategoryType.Expense, "utensils", "#EC4899");
        var salaryCategory = Category.CreateSystem(workspace.Id, "Maaş", "Salary", CategoryType.Income, "wallet", "#22C55E");

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        dbContext.Categories.AddRange(marketCategory, diningCategory, salaryCategory);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id, Locale = "tr-TR" };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);
        var quotaService = CreateQuotaService(dateTimeProvider, dailyReceiptLimit: 10);
        var fakeAiClient = new FakeAiServiceClient();

        var handler = new ScanReceiptCommandHandler(
            dbContext,
            accessService,
            currentUser,
            quotaService,
            fakeAiClient);

        var result = await handler.Handle(
            new ScanReceiptCommand(
                workspace.Id,
                ImageBase64: null,
                RawOcrText: "MIGROS TICARET A.S. TOPLAM 425,50 TL KDV 38,68"),
            CancellationToken.None);

        result.IsValidReceipt.Should().BeTrue();
        result.MerchantName.Should().Be("MİGROS TİCARET A.Ş.");
        result.TotalAmount.Should().Be(425.50m);
        result.SuggestedCategoryName.Should().Be("Market");
        result.SuggestedCategoryId.Should().Be(marketCategory.Id);
        result.RemainingDailyQuota.Should().Be(9);

        // Verify only Expense categories were forwarded to Python OCR
        fakeAiClient.LastReceiptRequest.Should().NotBeNull();
        fakeAiClient.LastReceiptRequest!.AvailableCategories.Should().Contain(["Market", "Yeme-İçme"]);
        fakeAiClient.LastReceiptRequest.AvailableCategories.Should().NotContain("Maaş");
    }

    [Fact]
    public async Task ExceedingDailyQuota_ShouldThrowDomainException_AndResetOnNextUtcDay()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider
        {
            UtcNow = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc)
        };

        var user = User.Create("quota@finsense.app", "hash", "Quota User", "tr-TR", dateTimeProvider.UtcNow);
        var workspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);
        var marketCategory = Category.CreateSystem(workspace.Id, "Market", "Groceries", CategoryType.Expense, "shopping-cart", "#10B981");

        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(workspace);
        dbContext.Categories.Add(marketCategory);
        await dbContext.SaveChangesAsync();

        var currentUser = new FakeCurrentUserService { UserId = user.Id, Locale = "tr-TR" };
        var accessService = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUser);
        var quotaService = CreateQuotaService(dateTimeProvider, dailyChatLimit: 2, dailyReceiptLimit: 1);
        var fakeAiClient = new FakeAiServiceClient();

        var chatHandler = new AskFinancialAssistantCommandHandler(
            dbContext, accessService, currentUser, dateTimeProvider, quotaService, fakeAiClient);
        var scanHandler = new ScanReceiptCommandHandler(
            dbContext, accessService, currentUser, quotaService, fakeAiClient);

        // Chat limit = 2 -> 1st (remaining 1), 2nd (remaining 0), 3rd throws DomainException
        var r1 = await chatHandler.Handle(new AskFinancialAssistantCommand(workspace.Id, "Durumum nasıl?"), CancellationToken.None);
        var r2 = await chatHandler.Handle(new AskFinancialAssistantCommand(workspace.Id, "Bütçem ne kadar?"), CancellationToken.None);
        r1.RemainingDailyQuota.Should().Be(1);
        r2.RemainingDailyQuota.Should().Be(0);

        var chatLimitAct = async () => await chatHandler.Handle(
            new AskFinancialAssistantCommand(workspace.Id, "Üçüncü soru"),
            CancellationToken.None);

        await chatLimitAct.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "AI_DAILY_QUOTA_EXCEEDED");

        // Receipt limit = 1 -> 1st (remaining 0), 2nd throws DomainException
        var s1 = await scanHandler.Handle(
            new ScanReceiptCommand(workspace.Id, null, "BIM FIŞ TOPLAM 100 TL"),
            CancellationToken.None);
        s1.RemainingDailyQuota.Should().Be(0);

        var receiptLimitAct = async () => await scanHandler.Handle(
            new ScanReceiptCommand(workspace.Id, null, "A101 FIŞ TOPLAM 50 TL"),
            CancellationToken.None);

        await receiptLimitAct.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "AI_DAILY_QUOTA_EXCEEDED");

        // Next UTC day -> quotas reset cleanly
        dateTimeProvider.UtcNow = dateTimeProvider.UtcNow.AddDays(1);
        var nextDayChat = await chatHandler.Handle(
            new AskFinancialAssistantCommand(workspace.Id, "Yeni gün özeti?"),
            CancellationToken.None);

        nextDayChat.RemainingDailyQuota.Should().Be(1);
    }

    [Fact]
    public async Task AskFinancialAssistant_GetHistory_And_ClearHistory_ShouldPersistAndManageConversationsPerUser()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider
        {
            UtcNow = new DateTime(2026, 11, 11, 14, 0, 0, DateTimeKind.Utc)
        };

        var userA = User.Create("chata@finsense.app", "hash", "User A", "tr-TR", dateTimeProvider.UtcNow);
        var userB = User.Create("chatb@finsense.app", "hash", "User B", "tr-TR", dateTimeProvider.UtcNow);
        var household = Workspace.CreateHousehold(userA.Id, "Ev Bütçesi", "TRY", dateTimeProvider.UtcNow);
        var userBMember = WorkspaceMember.Create(household.Id, userB.Id, WorkspaceRole.Viewer, dateTimeProvider.UtcNow);

        dbContext.Users.AddRange(userA, userB);
        dbContext.Workspaces.Add(household);
        dbContext.WorkspaceMembers.Add(userBMember);
        await dbContext.SaveChangesAsync();

        var quotaService = CreateQuotaService(dateTimeProvider, dailyChatLimit: 15);
        var fakeAiClient = new FakeAiServiceClient();

        // User A asks a long question (> 60 chars) followed by a second question
        var currentUserA = new FakeCurrentUserService { UserId = userA.Id, Locale = "tr-TR" };
        var accessA = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUserA);
        var chatHandlerA = new AskFinancialAssistantCommandHandler(
            dbContext, accessA, currentUserA, dateTimeProvider, quotaService, fakeAiClient);
        var historyHandlerA = new GetAiChatHistoryQueryHandler(dbContext, accessA, quotaService);
        var clearHandlerA = new ClearAiChatHistoryCommandHandler(dbContext, accessA);

        var longQuestion = "Bu ay market harcamalarım geçen aya göre nasıl değişti ve tasarruf için ne önerirsin?";
        var firstReply = await chatHandlerA.Handle(
            new AskFinancialAssistantCommand(household.Id, longQuestion),
            CancellationToken.None);

        firstReply.ConversationId.Should().NotBeEmpty();
        firstReply.RemainingDailyQuota.Should().Be(14);
        firstReply.CreatedAtUtc.Should().Be(dateTimeProvider.UtcNow.AddMilliseconds(1));

        dateTimeProvider.UtcNow = dateTimeProvider.UtcNow.AddMinutes(2);
        var secondReply = await chatHandlerA.Handle(
            new AskFinancialAssistantCommand(household.Id, "Bütçe aşımı var mı?"),
            CancellationToken.None);

        // Both turns reuse the same active conversation
        secondReply.ConversationId.Should().Be(firstReply.ConversationId);
        secondReply.RemainingDailyQuota.Should().Be(13);

        // User B starts a separate conversation in the same workspace
        var currentUserB = new FakeCurrentUserService { UserId = userB.Id, Locale = "tr-TR" };
        var accessB = TestApplicationHarness.CreateWorkspaceAccessService(dbContext, currentUserB);
        var chatHandlerB = new AskFinancialAssistantCommandHandler(
            dbContext, accessB, currentUserB, dateTimeProvider, quotaService, fakeAiClient);
        var historyHandlerB = new GetAiChatHistoryQueryHandler(dbContext, accessB, quotaService);

        var replyB = await chatHandlerB.Handle(
            new AskFinancialAssistantCommand(household.Id, "Toplam bakiyemiz nedir?"),
            CancellationToken.None);

        replyB.ConversationId.Should().NotBe(firstReply.ConversationId);

        // Verify persisted conversation & 4 messages for User A in chronological order
        var storedConvA = await dbContext.AiConversations
            .AsNoTracking()
            .SingleAsync(c => c.Id == firstReply.ConversationId);
        storedConvA.Title.Length.Should().BeLessThanOrEqualTo(60);

        var historyA = await historyHandlerA.Handle(new GetAiChatHistoryQuery(household.Id), CancellationToken.None);
        historyA.ConversationId.Should().Be(firstReply.ConversationId);
        historyA.RemainingDailyQuota.Should().Be(13);
        historyA.Messages.Should().HaveCount(4);
        historyA.Messages.Select(m => m.Role).Should().Equal("user", "assistant", "user", "assistant");
        historyA.Messages[0].Content.Should().Be(longQuestion);
        historyA.Messages[1].Content.Should().Be(fakeAiClient.ChatResponseToReturn.Answer);
        historyA.Messages[1].Intent.Should().Be("SPENDING_QUERY");
        historyA.Messages[1].IsFinanceRelated.Should().BeTrue();
        historyA.Messages[1].HighlightedCategory.Should().Be("Market");
        historyA.Messages[1].SuggestedAction.Should().Be(fakeAiClient.ChatResponseToReturn.SuggestedAction);
        historyA.Messages[1].ProviderUsed.Should().Be("smart-local");

        // Clear User A's history -> User A's conversation & messages removed, quota preserved, User B unaffected
        await clearHandlerA.Handle(new ClearAiChatHistoryCommand(household.Id), CancellationToken.None);

        var clearedHistoryA = await historyHandlerA.Handle(new GetAiChatHistoryQuery(household.Id), CancellationToken.None);
        clearedHistoryA.ConversationId.Should().BeNull();
        clearedHistoryA.Messages.Should().BeEmpty();
        clearedHistoryA.RemainingDailyQuota.Should().Be(13);

        var historyB = await historyHandlerB.Handle(new GetAiChatHistoryQuery(household.Id), CancellationToken.None);
        historyB.Messages.Should().HaveCount(2);
    }
}
