using FinSense.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinSense.Infrastructure.Services;

public sealed class RecurringTransactionsHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecurringTransactionsHostedService> _logger;

    public RecurringTransactionsHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<RecurringTransactionsHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Delay slightly on startup so Program.cs table initialization completes first
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        await RunProcessingPassAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunProcessingPassAsync(stoppingToken);
        }
    }

    private async Task RunProcessingPassAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<IRecurringTransactionProcessor>();
            var processed = await processor.ProcessDueTransactionsAsync(null, cancellationToken);

            if (processed > 0)
            {
                _logger.LogInformation(
                    "RecurringTransactionsHostedService executed {ProcessedCount} due recurring transaction(s).",
                    processed);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while processing due recurring transactions.");
        }
    }
}
