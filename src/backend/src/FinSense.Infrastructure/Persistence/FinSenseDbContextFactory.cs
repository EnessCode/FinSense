using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FinSense.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF Core CLI migrations without requiring runtime secrets.
/// </summary>
public class FinSenseDbContextFactory : IDesignTimeDbContextFactory<FinSenseDbContext>
{
    public FinSenseDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<FinSenseDbContext>();

        var connectionString = ResolveConnectionString(args);

        optionsBuilder
            .UseNpgsql(
                connectionString,
                npgsqlOptions => npgsqlOptions.MigrationsAssembly(typeof(FinSenseDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention();

        return new FinSenseDbContext(optionsBuilder.Options);
    }

    private static string ResolveConnectionString(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--connection", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        var envConnection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (!string.IsNullOrWhiteSpace(envConnection))
        {
            return envConnection;
        }

        var dotEnvValues = TryReadDotEnv();
        var host = dotEnvValues.GetValueOrDefault("POSTGRES_HOST", "localhost");
        var port = dotEnvValues.GetValueOrDefault("POSTGRES_PORT", "5432");
        var db = dotEnvValues.GetValueOrDefault("POSTGRES_DB", "finsense_db");
        var user = dotEnvValues.GetValueOrDefault("POSTGRES_USER", "finsense_user");
        var password = dotEnvValues.GetValueOrDefault("POSTGRES_PASSWORD", "change_me_local_pg_password");

        return $"Host={host};Port={port};Database={db};Username={user};Password={password}";
    }

    private static Dictionary<string, string> TryReadDotEnv()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (dir is not null)
        {
            var envFile = Path.Combine(dir.FullName, ".env");
            if (File.Exists(envFile))
            {
                foreach (var rawLine in File.ReadAllLines(envFile))
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                    {
                        continue;
                    }

                    var separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    var key = line[..separatorIndex].Trim();
                    var value = line[(separatorIndex + 1)..].Trim().Trim('"', '\'');
                    result[key] = value;
                }

                break;
            }

            dir = dir.Parent;
        }

        return result;
    }
}
