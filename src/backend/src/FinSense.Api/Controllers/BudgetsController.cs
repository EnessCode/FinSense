using FinSense.Api.Filters;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Features.Budgets.Commands;
using FinSense.Application.Features.Budgets.Contracts;
using FinSense.Application.Features.Budgets.Queries.GetBudgets;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinSense.Api.Controllers;

[ApiController]
[Route("api/v1/workspaces/{wsId:guid}/budgets")]
[Authorize]
public class BudgetsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IDateTimeProvider _dateTimeProvider;

    public BudgetsController(ISender sender, IDateTimeProvider dateTimeProvider)
    {
        _sender = sender;
        _dateTimeProvider = dateTimeProvider;
    }

    public sealed record UpsertBudgetRequest(
        Guid? CategoryId,
        short Year,
        short Month,
        decimal LimitAmount,
        short AlertThresholdPercent = 80);

    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    [ProducesResponseType(typeof(IReadOnlyList<BudgetDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BudgetDto>>> GetBudgets(
        [FromRoute] Guid wsId,
        [FromQuery] short? year = null,
        [FromQuery] short? month = null,
        CancellationToken cancellationToken = default)
    {
        var today = _dateTimeProvider.TodayIstanbul;
        var targetYear = year ?? (short)today.Year;
        var targetMonth = month ?? (short)today.Month;

        var budgets = await _sender.Send(
            new GetBudgetsQuery(wsId, targetYear, targetMonth),
            cancellationToken);

        return Ok(budgets);
    }

    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(BudgetDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BudgetDto>> UpsertBudget(
        [FromRoute] Guid wsId,
        [FromBody] UpsertBudgetRequest request,
        CancellationToken cancellationToken)
    {
        var budget = await _sender.Send(
            new UpsertBudgetCommand(
                wsId,
                request.CategoryId,
                request.Year,
                request.Month,
                request.LimitAmount,
                request.AlertThresholdPercent),
            cancellationToken);

        return Ok(budget);
    }

    [HttpDelete("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBudget(
        [FromRoute] Guid wsId,
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteBudgetCommand(wsId, id), cancellationToken);
        return NoContent();
    }
}
