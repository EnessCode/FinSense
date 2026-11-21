using FinSense.Application.Common.Interfaces;
using FinSense.Infrastructure.Identity;
using FinSense.Infrastructure.Persistence;
using FinSense.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FinSense.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<FinSenseDbContext>(options =>
        {
            options.UseNpgsql(
                    connectionString,
                    npgsqlOptions =>
                    {
                        npgsqlOptions.MigrationsAssembly(typeof(FinSenseDbContext).Assembly.FullName);
                        npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
                    })
                .UseSnakeCaseNamingConvention();
        });

        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<FinSenseDbContext>());

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IAiQuotaService, InMemoryAiQuotaService>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IEmailService, DevelopmentLogEmailService>();
        services.AddHostedService<RecurringTransactionsHostedService>();

        services.AddHttpClient<IMarketPriceService, YahooBinanceMarketPriceService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; FinSense/1.0)");
        });

        services.AddHttpClient<IAiServiceClient, AiServiceClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<AiOptions>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
                ? "http://localhost:8000/"
                : options.BaseUrl.TrimEnd('/') + "/";

            client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 25);

            var apiKey = !string.IsNullOrWhiteSpace(options.InternalApiKey) &&
                         options.InternalApiKey != "SET_VIA_USER_SECRETS_OR_ENV_VAR"
                ? options.InternalApiKey
                : "dev-internal-secret-key";

            client.DefaultRequestHeaders.Remove("X-Internal-Api-Key");
            client.DefaultRequestHeaders.Add("X-Internal-Api-Key", apiKey);
        });

        return services;
    }
}
