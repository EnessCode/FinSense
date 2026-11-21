using FinSense.Api.Filters;
using FinSense.Application.Features.Workspaces.Commands.AcceptInvitation;
using FinSense.Application.Features.Workspaces.Commands.CreateHouseholdWorkspace;
using FinSense.Application.Features.Workspaces.Commands.CreateInvitation;
using FinSense.Application.Features.Workspaces.Commands.DeleteHouseholdWorkspace;
using FinSense.Application.Features.Workspaces.Commands.RemoveMember;
using FinSense.Application.Features.Workspaces.Commands.UpdateMemberRole;
using FinSense.Application.Features.Workspaces.Contracts;
using FinSense.Application.Features.Workspaces.Queries.GetUserWorkspaces;
using FinSense.Application.Features.Workspaces.Queries.GetWorkspaceMembers;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinSense.Api.Controllers;

[ApiController]
[Route("api/v1/workspaces")]
[Authorize]
public class WorkspacesController : ControllerBase
{
    private readonly ISender _sender;

    public WorkspacesController(ISender sender)
    {
        _sender = sender;
    }

    public record CreateHouseholdWorkspaceRequest(string Name, string? DefaultCurrency = "TRY");

    public record CreateInvitationRequest(WorkspaceRole AssignedRole, string? InvitedEmail = null);

    public record AcceptInvitationRequest(string InvitationCode);

    public record UpdateMemberRoleRequest(WorkspaceRole Role);

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WorkspaceSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WorkspaceSummaryDto>>> GetUserWorkspaces(
        CancellationToken cancellationToken)
    {
        var workspaces = await _sender.Send(new GetUserWorkspacesQuery(), cancellationToken);
        return Ok(workspaces);
    }

    [HttpPost]
    [ProducesResponseType(typeof(WorkspaceSummaryDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<WorkspaceSummaryDto>> CreateHouseholdWorkspace(
        [FromBody] CreateHouseholdWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _sender.Send(
            new CreateHouseholdWorkspaceCommand(
                request.Name,
                string.IsNullOrWhiteSpace(request.DefaultCurrency) ? "TRY" : request.DefaultCurrency),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpDelete("{wsId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Owner)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteHouseholdWorkspace(
        [FromRoute] Guid wsId,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteHouseholdWorkspaceCommand(wsId), cancellationToken);
        return NoContent();
    }

    [HttpGet("{wsId:guid}/members")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    [ProducesResponseType(typeof(IReadOnlyList<WorkspaceMemberDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WorkspaceMemberDto>>> GetMembers(
        [FromRoute] Guid wsId,
        CancellationToken cancellationToken)
    {
        var members = await _sender.Send(new GetWorkspaceMembersQuery(wsId), cancellationToken);
        return Ok(members);
    }

    [HttpPost("{wsId:guid}/invitations")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(WorkspaceInvitationDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<WorkspaceInvitationDto>> CreateInvitation(
        [FromRoute] Guid wsId,
        [FromBody] CreateInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var invitation = await _sender.Send(
            new CreateWorkspaceInvitationCommand(wsId, request.AssignedRole, request.InvitedEmail),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, invitation);
    }

    [HttpPost("invitations/accept")]
    [ProducesResponseType(typeof(WorkspaceSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkspaceSummaryDto>> AcceptInvitation(
        [FromBody] AcceptInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var workspace = await _sender.Send(
            new AcceptWorkspaceInvitationCommand(request.InvitationCode),
            cancellationToken);

        return Ok(workspace);
    }

    [HttpPatch("{wsId:guid}/members/{userId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(WorkspaceMemberDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkspaceMemberDto>> UpdateMemberRole(
        [FromRoute] Guid wsId,
        [FromRoute] Guid userId,
        [FromBody] UpdateMemberRoleRequest request,
        CancellationToken cancellationToken)
    {
        var updatedMember = await _sender.Send(
            new UpdateWorkspaceMemberRoleCommand(wsId, userId, request.Role),
            cancellationToken);

        return Ok(updatedMember);
    }

    [HttpDelete("{wsId:guid}/members/{userId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveMember(
        [FromRoute] Guid wsId,
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new RemoveWorkspaceMemberCommand(wsId, userId), cancellationToken);
        return NoContent();
    }
}
