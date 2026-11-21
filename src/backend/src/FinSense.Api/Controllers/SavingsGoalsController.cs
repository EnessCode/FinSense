using FinSense.Api.Filters;
using FinSense.Application.Features.SavingsGoals.Commands;
using FinSense.Application.Features.SavingsGoals.Contracts;
using FinSense.Application.Features.SavingsGoals.Queries.GetSavingsGoals;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinSense.Api.Controllers;

[ApiController]
[Route("api/v1/workspaces/{wsId:guid}/savings-goals")]
[Authorize]
public class SavingsGoalsController : ControllerBase
{
    private readonly ISender _sender;

    public SavingsGoalsController(ISender sender)
    {
        _sender = sender;
    }

    public sealed record CreateSavingsGoalRequest(
        string Name,
        decimal TargetAmount,
        decimal CurrentAmount = 0m,
        Guid? LinkedWalletId = null,
        DateOnly? TargetDate = null);

    public sealed record UpdateSavingsGoalRequest(
        string Name,
        decimal TargetAmount,
        DateOnly? TargetDate = null,
        Guid? LinkedWalletId = null,
        decimal? CurrentAmount = null);

    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    [ProducesResponseType(typeof(IReadOnlyList<SavingsGoalDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SavingsGoalDto>>> GetSavingsGoals(
        [FromRoute] Guid wsId,
        CancellationToken cancellationToken)
    {
        var goals = await _sender.Send(new GetSavingsGoalsQuery(wsId), cancellationToken);
        return Ok(goals);
    }

    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(SavingsGoalDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SavingsGoalDto>> CreateSavingsGoal(
        [FromRoute] Guid wsId,
        [FromBody] CreateSavingsGoalRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _sender.Send(
            new CreateSavingsGoalCommand(
                wsId,
                request.Name,
                request.TargetAmount,
                request.CurrentAmount,
                request.LinkedWalletId,
                request.TargetDate),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(SavingsGoalDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SavingsGoalDto>> UpdateSavingsGoal(
        [FromRoute] Guid wsId,
        [FromRoute] Guid id,
        [FromBody] UpdateSavingsGoalRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _sender.Send(
            new UpdateSavingsGoalCommand(
                wsId,
                id,
                request.Name,
                request.TargetAmount,
                request.TargetDate,
                request.LinkedWalletId,
                request.CurrentAmount),
            cancellationToken);

        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSavingsGoal(
        [FromRoute] Guid wsId,
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteSavingsGoalCommand(wsId, id), cancellationToken);
        return NoContent();
    }
}
