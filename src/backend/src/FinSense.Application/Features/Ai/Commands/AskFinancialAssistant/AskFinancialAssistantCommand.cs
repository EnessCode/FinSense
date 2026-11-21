using System.Text.Json;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Ai.Contracts;
using FinSense.Application.Features.Categories.Contracts;
using FinSense.Application.Features.Investments.Contracts;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Ai.Commands.AskFinancialAssistant;

public sealed record AskFinancialAssistantCommand(
    Guid WorkspaceId,
    string Question) : IRequest<AiChatResponseDto>;

public sealed class AskFinancialAssistantCommandValidator : AbstractValidator<AskFinancialAssistantCommand>
{
    public AskFinancialAssistantCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();

        RuleFor(x => x.Question)
            .NotEmpty().WithMessage("Soru boş olamaz.")
            .MaximumLength(250).WithMessage("Soru en fazla 250 karakter olabilir.");
    }
}

public sealed class AskFinancialAssistantCommandHandler
    : IRequestHandler<AskFinancialAssistantCommand, AiChatResponseDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAiQuotaService _aiQuotaService;
    private readonly IAiServiceClient _aiServiceClient;
    private readonly IRecurringTransactionProcessor? _recurringProcessor;
    private readonly IMarketPriceService? _marketPriceService;

    public AskFinancialAssistantCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider,
        IAiQuotaService aiQuotaService,
        IAiServiceClient aiServiceClient,
        IRecurringTransactionProcessor? recurringProcessor = null,
        IMarketPriceService? marketPriceService = null)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
        _aiQuotaService = aiQuotaService;
        _aiServiceClient = aiServiceClient;
        _recurringProcessor = recurringProcessor;
        _marketPriceService = marketPriceService;
    }

    public async Task<AiChatResponseDto> Handle(
        AskFinancialAssistantCommand request,
        CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(
            request.WorkspaceId,
            WorkspaceRole.Viewer,
            cancellationToken);

        var remainingQuota = _aiQuotaService.ConsumeDailyQuota(access.UserId, AiQuotaType.Chat);

        if (_recurringProcessor is not null)
        {
            await _recurringProcessor.ProcessDueTransactionsAsync(request.WorkspaceId, cancellationToken);
        }

        var locale = _currentUserService.Locale;
        var isEnglish = CategoryLocalization.IsEnglish(locale);
        var today = _dateTimeProvider.TodayIstanbul;
        var periodStart = new DateOnly(today.Year, today.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        var totalBalance = await _dbContext.Wallets
            .AsNoTracking()
            .Where(w => w.WorkspaceId == request.WorkspaceId && !w.IsDeleted && !w.IsArchived)
            .SumAsync(w => (decimal?)w.CurrentBalance, cancellationToken) ?? 0m;

        // Strictly exclude Transfer transactions so inter-wallet transfers never inflate income/expense totals
        var monthlyTransactions = await _dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.WorkspaceId == request.WorkspaceId &&
                        !t.IsDeleted &&
                        t.Type != TransactionType.Transfer &&
                        t.TransactionDate >= periodStart &&
                        t.TransactionDate <= periodEnd)
            .Select(t => new
            {
                t.Type,
                t.Amount,
                t.CategoryId,
                CategoryName = t.Category != null
                    ? (isEnglish && t.Category.NameEn != null ? t.Category.NameEn : t.Category.NameTr)
                    : null,
                MemberName = t.PaidByUser != null
                    ? t.PaidByUser.DisplayName
                    : (t.CreatedByUser != null ? t.CreatedByUser.DisplayName : null)
            })
            .ToListAsync(cancellationToken);

        var monthlyIncome = monthlyTransactions
            .Where(t => t.Type == TransactionType.Income)
            .Sum(t => t.Amount);

        var expenseTransactions = monthlyTransactions
            .Where(t => t.Type == TransactionType.Expense)
            .ToList();

        var monthlyExpense = expenseTransactions.Sum(t => t.Amount);
        var netCashFlow = decimal.Round(monthlyIncome - monthlyExpense, 4, MidpointRounding.AwayFromZero);
        var savingsRatePercent = monthlyIncome > 0m
            ? decimal.Round((netCashFlow / monthlyIncome) * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var expenseByCategoryId = expenseTransactions
            .Where(t => t.CategoryId.HasValue)
            .GroupBy(t => new { CategoryId = t.CategoryId!.Value, Name = t.CategoryName ?? "-" })
            .Select(g => new
            {
                g.Key.CategoryId,
                g.Key.Name,
                Amount = g.Sum(x => x.Amount),
                Count = g.Count()
            })
            .OrderByDescending(x => x.Amount)
            .ToList();

        var topCategories = expenseByCategoryId
            .Take(8)
            .Select(c => new AiCategorySpendItemDto(
                c.Name,
                c.Amount,
                monthlyExpense > 0m
                    ? decimal.Round((c.Amount / monthlyExpense) * 100m, 2, MidpointRounding.AwayFromZero)
                    : 0m,
                c.Count))
            .ToList();

        var categorySpendLookup = expenseByCategoryId.ToDictionary(x => x.CategoryId, x => x.Amount);

        var budgets = await _dbContext.Budgets
            .AsNoTracking()
            .Where(b => b.WorkspaceId == request.WorkspaceId &&
                        b.Year == (short)today.Year &&
                        b.Month == (short)today.Month &&
                        !b.IsDeleted)
            .Select(b => new
            {
                Budget = b,
                CategoryName = b.Category != null
                    ? (isEnglish && b.Category.NameEn != null ? b.Category.NameEn : b.Category.NameTr)
                    : (isEnglish ? "Overall Budget" : "Genel Bütçe")
            })
            .ToListAsync(cancellationToken);

        var budgetAlerts = budgets
            .Select(row =>
            {
                var spent = row.Budget.CategoryId.HasValue
                    ? categorySpendLookup.GetValueOrDefault(row.Budget.CategoryId.Value, 0m)
                    : monthlyExpense;

                var utilization = row.Budget.CalculateUtilizationPercent(spent);
                var status = row.Budget.EvaluateStatus(spent).ToString();

                return new AiBudgetAlertItemDto(
                    row.CategoryName,
                    row.Budget.LimitAmount,
                    spent,
                    utilization,
                    status);
            })
            .OrderByDescending(b => b.UtilizationPercent)
            .ToList();

        IReadOnlyList<AiMemberSpendItemDto> memberBreakdown = [];
        if (access.IsHousehold)
        {
            var fallbackMemberName = isEnglish ? "Member" : "Üye";
            memberBreakdown = expenseTransactions
                .GroupBy(t => t.MemberName ?? fallbackMemberName)
                .Select(g =>
                {
                    var amount = g.Sum(x => x.Amount);
                    return new AiMemberSpendItemDto(
                        g.Key,
                        amount,
                        monthlyExpense > 0m
                            ? decimal.Round((amount / monthlyExpense) * 100m, 2, MidpointRounding.AwayFromZero)
                            : 0m);
                })
                .OrderByDescending(m => m.Amount)
                .ToList();
        }

        var activeRecurring = await _dbContext.RecurringTransactions
            .AsNoTracking()
            .Where(r => r.WorkspaceId == request.WorkspaceId && r.IsActive)
            .OrderBy(r => r.NextExecutionDateUtc)
            .Select(r => new AiRecurringObligationItemDto(
                r.Title,
                r.Type.ToString(),
                r.Amount,
                r.DayOfMonth,
                r.TotalOccurrences,
                r.CompletedOccurrences,
                r.NextExecutionDateUtc))
            .ToListAsync(cancellationToken);

        var monthlyFixedIncome = activeRecurring
            .Where(r => r.Type == nameof(TransactionType.Income))
            .Sum(r => r.Amount);

        var monthlyFixedExpense = activeRecurring
            .Where(r => r.Type == nameof(TransactionType.Expense))
            .Sum(r => r.Amount);

        var investmentLots = await _dbContext.InvestmentTransactions
            .AsNoTracking()
            .Where(i => i.WorkspaceId == request.WorkspaceId)
            .ToListAsync(cancellationToken);

        AiInvestmentPortfolioDto? investmentPortfolio = null;
        if (investmentLots.Count > 0)
        {
            var livePrices = await InvestmentPortfolioCalculator.FetchLivePricesAsync(
                _marketPriceService,
                investmentLots,
                cancellationToken);
            var portfolio = InvestmentPortfolioCalculator.BuildPortfolio(investmentLots, livePrices);
            investmentPortfolio = new AiInvestmentPortfolioDto(
                portfolio.TotalInvestedCost,
                portfolio.TotalCurrentValue,
                portfolio.TotalProfitLossAmount,
                portfolio.TotalProfitLossPercent,
                portfolio.Holdings
                    .Take(5)
                    .Select(h => new AiInvestmentHoldingItemDto(
                        h.Symbol,
                        h.AssetName,
                        h.Category.ToString(),
                        h.TotalQuantity,
                        h.TotalCost,
                        h.CurrentValue,
                        h.ProfitLossAmount,
                        h.ProfitLossPercent))
                    .ToList());
        }

        var financialContext = new FinancialContextDto(
            totalBalance,
            monthlyIncome,
            monthlyExpense,
            netCashFlow,
            savingsRatePercent,
            topCategories,
            budgetAlerts,
            memberBreakdown,
            monthlyFixedIncome,
            monthlyFixedExpense,
            activeRecurring,
            investmentPortfolio);

        var internalRequest = new InternalAiChatRequest(
            request.Question.Trim(),
            locale,
            access.WorkspaceType.ToString(),
            financialContext);

        var aiResponse = await _aiServiceClient.ChatAsync(internalRequest, cancellationToken);

        var userMessageTimestamp = _dateTimeProvider.UtcNow;
        var assistantMessageTimestamp = userMessageTimestamp.AddMilliseconds(1);

        var conversation = await _dbContext.AiConversations
            .Where(c => c.WorkspaceId == request.WorkspaceId && c.UserId == access.UserId)
            .OrderByDescending(c => c.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (conversation is null)
        {
            var trimmedQuestion = request.Question.Trim();
            var title = trimmedQuestion.Length <= 60
                ? trimmedQuestion
                : trimmedQuestion[..57] + "...";

            conversation = AiConversation.Create(
                request.WorkspaceId,
                access.UserId,
                title,
                userMessageTimestamp);

            _dbContext.AiConversations.Add(conversation);
        }

        var userMessage = conversation.AddMessage(
            AiMessageRole.User,
            request.Question,
            referencedMetricsJson: null,
            providerUsed: null,
            utcNow: userMessageTimestamp);

        var metadataJson = JsonSerializer.Serialize(
            new AiMessageMetadata(
                aiResponse.IsFinanceRelated,
                aiResponse.Intent,
                aiResponse.HighlightedCategory,
                aiResponse.SuggestedAction));

        var assistantMessage = conversation.AddMessage(
            AiMessageRole.Assistant,
            aiResponse.Answer,
            referencedMetricsJson: metadataJson,
            providerUsed: aiResponse.ProviderUsed,
            utcNow: assistantMessageTimestamp);

        // Explicitly register new AiMessage rows as Added so EF Core inserts them reliably on existing conversations
        _dbContext.AiMessages.Add(userMessage);
        _dbContext.AiMessages.Add(assistantMessage);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AiChatResponseDto(
            conversation.Id,
            aiResponse.IsFinanceRelated,
            aiResponse.Intent,
            aiResponse.Answer,
            aiResponse.HighlightedCategory,
            aiResponse.SuggestedAction,
            aiResponse.ProviderUsed,
            remainingQuota,
            assistantMessage.CreatedAtUtc);
    }
}
