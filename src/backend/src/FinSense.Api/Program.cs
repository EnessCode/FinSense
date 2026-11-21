using System.Text;
using System.Text.Json.Serialization;
using FinSense.Api.Middleware;
using FinSense.Api.Services;
using FinSense.Application;
using FinSense.Application.Common.Interfaces;
using FinSense.Infrastructure;
using FinSense.Infrastructure.Identity;
using FinSense.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IWorkspaceContext, WorkspaceContext>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalDev", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidateIssuerSigningKey = true,
            IssuerSigningKeyResolver = (_, _, _, _) =>
            {
                var currentKey = builder.Configuration[$"{JwtOptions.SectionName}:{nameof(JwtOptions.SigningKey)}"];
                if (string.IsNullOrWhiteSpace(currentKey) || currentKey == "SET_VIA_USER_SECRETS_OR_ENV_VAR")
                {
                    return [];
                }

                return [new SymmetricSecurityKey(Encoding.UTF8.GetBytes(currentKey))];
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FinSense API",
        Version = "v1",
        Description = "FinSense Personal & Household Finance Management Public Gateway API"
    });

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Enter JWT Bearer token: Bearer {token}",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };

    options.AddSecurityDefinition("Bearer", securityScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { securityScheme, Array.Empty<string>() }
    });
});

var app = builder.Build();

await EnsureStartupTablesAsync(app);

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors("AllowLocalDev");
}
else
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "FinSense.Api" }));

app.Run();

static async Task EnsureStartupTablesAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("StartupSchemaInitializer");

    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<FinSenseDbContext>();
        if (!dbContext.Database.IsNpgsql())
        {
            return;
        }

        const string sql = """
            CREATE TABLE IF NOT EXISTS recurring_transactions (
                id uuid NOT NULL,
                workspace_id uuid NOT NULL,
                wallet_id uuid NOT NULL,
                category_id uuid NOT NULL,
                created_by_user_id uuid NOT NULL,
                type smallint NOT NULL,
                amount numeric(18,2) NOT NULL,
                currency character varying(3) NOT NULL DEFAULT 'TRY',
                title character varying(150) NOT NULL,
                day_of_month integer NOT NULL,
                total_occurrences integer NULL,
                completed_occurrences integer NOT NULL DEFAULT 0,
                start_date_utc timestamp with time zone NOT NULL,
                next_execution_date_utc timestamp with time zone NOT NULL,
                last_executed_at_utc timestamp with time zone NULL,
                is_active boolean NOT NULL DEFAULT TRUE,
                created_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT pk_recurring_transactions PRIMARY KEY (id),
                CONSTRAINT "CK_recurring_transactions_amount_positive" CHECK (amount > 0),
                CONSTRAINT "CK_recurring_transactions_day_of_month_range" CHECK (day_of_month BETWEEN 1 AND 31),
                CONSTRAINT "CK_recurring_transactions_type_valid" CHECK (type IN (1, 2)),
                CONSTRAINT fk_recurring_transactions_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES workspaces (id) ON DELETE CASCADE,
                CONSTRAINT fk_recurring_transactions_wallets_wallet_id FOREIGN KEY (wallet_id) REFERENCES wallets (id) ON DELETE CASCADE,
                CONSTRAINT fk_recurring_transactions_categories_category_id FOREIGN KEY (category_id) REFERENCES categories (id) ON DELETE RESTRICT,
                CONSTRAINT fk_recurring_transactions_users_created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS "IX_recurring_transactions_workspace_active_next"
                ON recurring_transactions (workspace_id, is_active, next_execution_date_utc);

            CREATE TABLE IF NOT EXISTS investment_transactions (
                id uuid NOT NULL,
                workspace_id uuid NOT NULL,
                created_by_user_id uuid NOT NULL,
                category smallint NOT NULL,
                symbol character varying(24) NOT NULL,
                asset_name character varying(100) NOT NULL,
                quantity numeric(18,6) NOT NULL,
                unit_price numeric(18,4) NOT NULL,
                total_cost numeric(18,2) NOT NULL,
                current_unit_price numeric(18,4) NOT NULL,
                currency character varying(3) NOT NULL DEFAULT 'TRY',
                purchase_date_utc timestamp with time zone NOT NULL,
                notes character varying(250) NULL,
                created_at_utc timestamp with time zone NOT NULL,
                CONSTRAINT pk_investment_transactions PRIMARY KEY (id),
                CONSTRAINT "CK_investment_transactions_quantity_positive" CHECK (quantity > 0),
                CONSTRAINT "CK_investment_transactions_unit_price_positive" CHECK (unit_price > 0),
                CONSTRAINT "CK_investment_transactions_total_cost_positive" CHECK (total_cost > 0),
                CONSTRAINT "CK_investment_transactions_current_unit_price_positive" CHECK (current_unit_price > 0),
                CONSTRAINT "CK_investment_transactions_category_valid" CHECK (category BETWEEN 1 AND 5),
                CONSTRAINT fk_investment_transactions_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES workspaces (id) ON DELETE CASCADE,
                CONSTRAINT fk_investment_transactions_users_created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS "IX_investment_transactions_workspace_symbol"
                ON investment_transactions (workspace_id, symbol);

            CREATE INDEX IF NOT EXISTS "IX_investment_transactions_workspace_date"
                ON investment_transactions (workspace_id, purchase_date_utc DESC);
            """;

        await dbContext.Database.ExecuteSqlRawAsync(sql);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not verify or create startup tables on PostgreSQL.");
    }
}

public partial class Program { }
