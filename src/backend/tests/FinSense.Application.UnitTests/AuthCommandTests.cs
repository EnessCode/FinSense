using FinSense.Application.Common.Exceptions;
using FinSense.Application.Features.Auth.Commands.Refresh;
using FinSense.Application.Features.Auth.Commands.Register;
using FinSense.Application.UnitTests.Common;
using FinSense.Domain.Enums;
using FinSense.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.UnitTests;

public class AuthCommandTests
{
    [Fact]
    public async Task RegisterCommand_ShouldProvisionUser_PersonalWorkspace_DefaultCategories_AndHashedRefreshToken()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var passwordHasher = new BCryptPasswordHasher();
        var tokenService = TestApplicationHarness.CreateTokenService(dateTimeProvider);

        var handler = new RegisterCommandHandler(dbContext, passwordHasher, tokenService, dateTimeProvider);

        var response = await handler.Handle(
            new RegisterCommand("  Ayse.Yilmaz@Example.com ", "StrongPass123", "Ayşe Yılmaz", "tr-TR"),
            CancellationToken.None);

        response.User.Email.Should().Be("ayse.yilmaz@example.com");
        response.User.DisplayName.Should().Be("Ayşe Yılmaz");
        response.User.PreferredLocale.Should().Be("tr-TR");
        response.User.PersonalWorkspaceId.Should().NotBeEmpty();
        response.Tokens.AccessToken.Should().NotBeNullOrWhiteSpace();
        response.Tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();

        // Verify Personal Workspace & Owner Membership
        var workspace = await dbContext.Workspaces
            .Include(w => w.Members)
            .SingleAsync(w => w.Id == response.User.PersonalWorkspaceId);

        workspace.Type.Should().Be(WorkspaceType.Personal);
        workspace.Name.Should().Be("Kişisel Alanım");
        workspace.DefaultCurrency.Should().Be("TRY");
        workspace.Members.Should().ContainSingle(m =>
            m.UserId == response.User.Id && m.Role == WorkspaceRole.Owner);

        // Verify Default System Categories (8 Expense + 4 Income = 12)
        var categories = await dbContext.Categories
            .Where(c => c.WorkspaceId == workspace.Id)
            .ToListAsync();

        categories.Should().HaveCount(12);
        categories.Should().OnlyContain(c => c.IsSystem && !c.IsDeleted);
        categories.Count(c => c.Type == CategoryType.Expense).Should().Be(8);
        categories.Count(c => c.Type == CategoryType.Income).Should().Be(4);
        categories.Should().Contain(c => c.NameTr == "Market" && c.NameEn == "Groceries");
        categories.Should().Contain(c => c.NameTr == "Maaş" && c.NameEn == "Salary");

        // Verify Refresh Token is stored hashed (never raw)
        var storedRefreshToken = await dbContext.RefreshTokens.SingleAsync(rt => rt.UserId == response.User.Id);
        storedRefreshToken.TokenHash.Should().NotBe(response.Tokens.RefreshToken);
        storedRefreshToken.TokenHash.Should().Be(tokenService.ComputeTokenHash(response.Tokens.RefreshToken));
    }

    [Fact]
    public async Task RegisterCommand_WithDuplicateEmail_ShouldThrowConflictException()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var passwordHasher = new BCryptPasswordHasher();
        var tokenService = TestApplicationHarness.CreateTokenService(dateTimeProvider);

        var handler = new RegisterCommandHandler(dbContext, passwordHasher, tokenService, dateTimeProvider);

        await handler.Handle(
            new RegisterCommand("enes@finsense.app", "StrongPass123", "Enes"),
            CancellationToken.None);

        var act = async () => await handler.Handle(
            new RegisterCommand("ENES@finsense.app", "AnotherPass123", "Enes 2"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.ErrorCode == "EMAIL_ALREADY_REGISTERED");
    }

    [Fact]
    public async Task RefreshTokenCommand_ShouldRotateToken_And_RevokeEntireFamilyOnTokenReuse()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var passwordHasher = new BCryptPasswordHasher();
        var tokenService = TestApplicationHarness.CreateTokenService(dateTimeProvider);

        var registerHandler = new RegisterCommandHandler(dbContext, passwordHasher, tokenService, dateTimeProvider);
        var refreshHandler = new RefreshTokenCommandHandler(dbContext, tokenService, dateTimeProvider);

        var initialAuth = await registerHandler.Handle(
            new RegisterCommand("security@finsense.app", "StrongPass123", "Security User"),
            CancellationToken.None);

        var firstRefreshToken = initialAuth.Tokens.RefreshToken;

        // 1. Legitimate rotation
        dateTimeProvider.UtcNow = dateTimeProvider.UtcNow.AddMinutes(10);
        var rotatedTokens = await refreshHandler.Handle(
            new RefreshTokenCommand(firstRefreshToken),
            CancellationToken.None);

        rotatedTokens.RefreshToken.Should().NotBe(firstRefreshToken);

        var allFamilyTokensAfterRotation = await dbContext.RefreshTokens
            .Where(rt => rt.UserId == initialAuth.User.Id)
            .ToListAsync();

        allFamilyTokensAfterRotation.Should().HaveCount(2);
        allFamilyTokensAfterRotation.Select(rt => rt.TokenFamilyId).Distinct().Should().ContainSingle();

        // 2. Reuse Detection: Attacker or stale client replays the already-rotated firstRefreshToken
        dateTimeProvider.UtcNow = dateTimeProvider.UtcNow.AddMinutes(1);
        var reuseAct = async () => await refreshHandler.Handle(
            new RefreshTokenCommand(firstRefreshToken),
            CancellationToken.None);

        await reuseAct.Should().ThrowAsync<UnauthorizedException>()
            .Where(e => e.ErrorCode == "REFRESH_TOKEN_REUSED");

        // Verify the entire token family (including the newly rotated successor token) is now revoked!
        var familyTokensAfterReuse = await dbContext.RefreshTokens
            .Where(rt => rt.UserId == initialAuth.User.Id)
            .ToListAsync();

        familyTokensAfterReuse.Should().OnlyContain(rt => rt.IsRevoked);
    }
}
