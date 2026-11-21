using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Domain.Enums;
using FinSense.Infrastructure.Identity;
using FinSense.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinSense.Application.UnitTests.Common;

public static class TestApplicationHarness
{
    public static FinSenseDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<FinSenseDbContext>()
            .UseInMemoryDatabase(databaseName: $"finsense_test_{Guid.NewGuid():N}")
            .Options;

        return new FinSenseDbContext(options);
    }

    public static IWorkspaceAccessService CreateWorkspaceAccessService(
        IAppDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        return new WorkspaceAccessService(dbContext, currentUserService, new FakeWorkspaceContext());
    }

    public static ITokenService CreateTokenService(IDateTimeProvider dateTimeProvider)
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "https://finsense.app",
            Audience = "https://finsense.app",
            SigningKey = "unit_test_jwt_signing_key_with_at_least_32_bytes_length!",
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 30
        });

        return new JwtTokenService(jwtOptions, dateTimeProvider);
    }
}

public sealed class FakeDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    public DateOnly TodayIstanbul => DateOnly.FromDateTime(UtcNow.AddHours(3));
}

public sealed class FakeCurrentUserService : ICurrentUserService
{
    public Guid? UserId { get; set; }
    public string? Email { get; set; } = "test@finsense.app";
    public string Locale { get; set; } = "tr-TR";
    public bool IsAuthenticated => UserId.HasValue;

    public Guid GetRequiredUserId() =>
        UserId ?? throw new UnauthorizedException("AUTHENTICATION_REQUIRED", "Oturum açılmadı.");
}

public sealed class FakeWorkspaceContext : IWorkspaceContext
{
    public Guid WorkspaceId { get; private set; }
    public WorkspaceType WorkspaceType { get; private set; }
    public WorkspaceRole Role { get; private set; }
    public bool IsInitialized { get; private set; }

    public void Initialize(Guid workspaceId, WorkspaceType workspaceType, WorkspaceRole role)
    {
        WorkspaceId = workspaceId;
        WorkspaceType = workspaceType;
        Role = role;
        IsInitialized = true;
    }
}

public sealed class FakeEmailService : IEmailService
{
    public List<(string Email, string WorkspaceName, string Code, WorkspaceRole Role)> SentInvitations { get; } = [];

    public Task SendWorkspaceInvitationAsync(
        string recipientEmail,
        string workspaceName,
        string invitationCode,
        WorkspaceRole assignedRole,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        SentInvitations.Add((recipientEmail, workspaceName, invitationCode, assignedRole));
        return Task.CompletedTask;
    }
}
