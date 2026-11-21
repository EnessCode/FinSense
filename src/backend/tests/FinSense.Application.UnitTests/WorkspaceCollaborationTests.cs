using FinSense.Application.Common.Exceptions;
using FinSense.Application.Features.Workspaces.Commands.AcceptInvitation;
using FinSense.Application.Features.Workspaces.Commands.CreateHouseholdWorkspace;
using FinSense.Application.Features.Workspaces.Commands.CreateInvitation;
using FinSense.Application.Features.Workspaces.Commands.DeleteHouseholdWorkspace;
using FinSense.Application.Features.Workspaces.Commands.RemoveMember;
using FinSense.Application.UnitTests.Common;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.UnitTests;

public class WorkspaceCollaborationTests
{
    [Fact]
    public async Task CreateInvitation_And_AcceptInvitation_ShouldEnforceHouseholdAndExpiryRules()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var currentUser = new FakeCurrentUserService();
        var emailService = new FakeEmailService();

        var ownerUser = User.Create("owner@finsense.app", "hash", "Owner User", "tr-TR", dateTimeProvider.UtcNow);
        var partnerUser = User.Create("partner@finsense.app", "hash", "Partner User", "tr-TR", dateTimeProvider.UtcNow);
        dbContext.Users.AddRange(ownerUser, partnerUser);
        await dbContext.SaveChangesAsync();

        currentUser.UserId = ownerUser.Id;

        // 1. Owner creates a Household Workspace
        var createHouseholdHandler = new CreateHouseholdWorkspaceCommandHandler(dbContext, currentUser, dateTimeProvider);
        var household = await createHouseholdHandler.Handle(
            new CreateHouseholdWorkspaceCommand("Ev Bütçesi", "TRY"),
            CancellationToken.None);

        household.Type.Should().Be(WorkspaceType.Household);
        household.CurrentUserRole.Should().Be(WorkspaceRole.Owner);

        // 2. Owner generates an invitation for Partner as Admin
        var createInviteHandler = new CreateWorkspaceInvitationCommandHandler(dbContext, currentUser, dateTimeProvider, emailService);
        var invitationDto = await createInviteHandler.Handle(
            new CreateWorkspaceInvitationCommand(household.Id, WorkspaceRole.Admin, "partner@finsense.app"),
            CancellationToken.None);

        invitationDto.InvitationCode.Should().HaveLength(8);
        emailService.SentInvitations.Should().ContainSingle(i => i.Code == invitationDto.InvitationCode);

        // 3. Partner accepts the invitation
        currentUser.UserId = partnerUser.Id;
        var acceptHandler = new AcceptWorkspaceInvitationCommandHandler(dbContext, currentUser, dateTimeProvider);
        var joinedWorkspace = await acceptHandler.Handle(
            new AcceptWorkspaceInvitationCommand(invitationDto.InvitationCode.ToLowerInvariant()),
            CancellationToken.None);

        joinedWorkspace.Id.Should().Be(household.Id);
        joinedWorkspace.CurrentUserRole.Should().Be(WorkspaceRole.Admin);

        var members = await dbContext.WorkspaceMembers
            .Where(wm => wm.WorkspaceId == household.Id)
            .ToListAsync();
        members.Should().HaveCount(2);

        // 4. Reusing the same invitation code should fail
        var reuseAct = async () => await acceptHandler.Handle(
            new AcceptWorkspaceInvitationCommand(invitationDto.InvitationCode),
            CancellationToken.None);

        await reuseAct.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVITATION_ALREADY_ACCEPTED");
    }

    [Fact]
    public async Task CreateInvitation_OnPersonalWorkspace_ShouldThrowDomainException()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var currentUser = new FakeCurrentUserService();
        var emailService = new FakeEmailService();

        var user = User.Create("user@finsense.app", "hash", "User", "tr-TR", dateTimeProvider.UtcNow);
        var personalWorkspace = Workspace.CreatePersonal(user.Id, utcNow: dateTimeProvider.UtcNow);
        dbContext.Users.Add(user);
        dbContext.Workspaces.Add(personalWorkspace);
        await dbContext.SaveChangesAsync();

        currentUser.UserId = user.Id;

        var createInviteHandler = new CreateWorkspaceInvitationCommandHandler(dbContext, currentUser, dateTimeProvider, emailService);
        var act = async () => await createInviteHandler.Handle(
            new CreateWorkspaceInvitationCommand(personalWorkspace.Id, WorkspaceRole.Viewer),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "PERSONAL_WORKSPACE_INVITATION_FORBIDDEN");
    }

    [Fact]
    public async Task AcceptInvitation_WhenExpired_ShouldThrowDomainException()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var currentUser = new FakeCurrentUserService();
        var emailService = new FakeEmailService();

        var owner = User.Create("owner@finsense.app", "hash", "Owner", "tr-TR", dateTimeProvider.UtcNow);
        var invitee = User.Create("invitee@finsense.app", "hash", "Invitee", "tr-TR", dateTimeProvider.UtcNow);
        var household = Workspace.CreateHousehold(owner.Id, "Ev Bütçesi", "TRY", dateTimeProvider.UtcNow);

        dbContext.Users.AddRange(owner, invitee);
        dbContext.Workspaces.Add(household);
        await dbContext.SaveChangesAsync();

        currentUser.UserId = owner.Id;
        var createInviteHandler = new CreateWorkspaceInvitationCommandHandler(dbContext, currentUser, dateTimeProvider, emailService);
        var invitation = await createInviteHandler.Handle(
            new CreateWorkspaceInvitationCommand(household.Id, WorkspaceRole.Contributor),
            CancellationToken.None);

        // Advance clock past 48-hour expiration window
        dateTimeProvider.UtcNow = dateTimeProvider.UtcNow.AddHours(49);
        currentUser.UserId = invitee.Id;

        var acceptHandler = new AcceptWorkspaceInvitationCommandHandler(dbContext, currentUser, dateTimeProvider);
        var act = async () => await acceptHandler.Handle(
            new AcceptWorkspaceInvitationCommand(invitation.InvitationCode),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "INVITATION_EXPIRED");
    }

    [Fact]
    public async Task DeleteWorkspace_And_RemoveMember_ShouldEnforcePersonalAndOwnerGuards()
    {
        using var dbContext = TestApplicationHarness.CreateInMemoryDbContext();
        var dateTimeProvider = new FakeDateTimeProvider();
        var currentUser = new FakeCurrentUserService();

        var owner = User.Create("owner@finsense.app", "hash", "Owner", "tr-TR", dateTimeProvider.UtcNow);
        var contributor = User.Create("contributor@finsense.app", "hash", "Contributor", "tr-TR", dateTimeProvider.UtcNow);
        var personalWorkspace = Workspace.CreatePersonal(owner.Id, utcNow: dateTimeProvider.UtcNow);
        var household = Workspace.CreateHousehold(owner.Id, "Ev Bütçesi", "TRY", dateTimeProvider.UtcNow);
        var contributorMember = WorkspaceMember.Create(household.Id, contributor.Id, WorkspaceRole.Contributor, dateTimeProvider.UtcNow);

        dbContext.Users.AddRange(owner, contributor);
        dbContext.Workspaces.AddRange(personalWorkspace, household);
        dbContext.WorkspaceMembers.Add(contributorMember);
        await dbContext.SaveChangesAsync();

        // 1. Owner cannot delete Personal Workspace via DeleteHouseholdWorkspaceCommand
        currentUser.UserId = owner.Id;
        var deleteHandler = new DeleteHouseholdWorkspaceCommandHandler(dbContext, currentUser);
        var deletePersonalAct = async () => await deleteHandler.Handle(
            new DeleteHouseholdWorkspaceCommand(personalWorkspace.Id),
            CancellationToken.None);

        await deletePersonalAct.Should().ThrowAsync<DomainException>()
            .Where(e => e.ErrorCode == "PERSONAL_WORKSPACE_CANNOT_BE_DELETED");

        // 2. Contributor cannot remove Owner
        currentUser.UserId = contributor.Id;
        var removeMemberHandler = new RemoveWorkspaceMemberCommandHandler(dbContext, currentUser, dateTimeProvider);
        var contributorRemoveOwnerAct = async () => await removeMemberHandler.Handle(
            new RemoveWorkspaceMemberCommand(household.Id, owner.Id),
            CancellationToken.None);

        await contributorRemoveOwnerAct.Should().ThrowAsync<ForbiddenAccessException>()
            .Where(e => e.ErrorCode == "WORKSPACE_INSUFFICIENT_ROLE");

        // 3. Contributor CAN leave the household themselves (Self-Leave)
        await removeMemberHandler.Handle(
            new RemoveWorkspaceMemberCommand(household.Id, contributor.Id),
            CancellationToken.None);

        var remainingMembers = await dbContext.WorkspaceMembers
            .Where(wm => wm.WorkspaceId == household.Id)
            .ToListAsync();
        remainingMembers.Should().ContainSingle(wm => wm.UserId == owner.Id);
    }
}
