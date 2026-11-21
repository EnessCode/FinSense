using FinSense.Api.Filters;
using FinSense.Application.Features.RecurringTransactions.Commands;
using FinSense.Application.Features.RecurringTransactions.Contracts;
using FinSense.Application.Features.RecurringTransactions.Queries.GetRecurringTransactions;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinSense.Api.Controllers;

[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/recurring-transactions")]
[Authorize]
public class RecurringTransactionsController : ControllerBase
{
    private readonly ISender _sender;

    public RecurringTransactionsController(ISender sender)
    {
        _sender = sender;
    }

    public sealed record CreateRecurringTransactionRequest(
        Guid WalletId,
        Guid CategoryId,
        TransactionType Type,
        decimal Amount,
        string Title,
        int DayOfMonth,
        int? TotalOccurrences = null,
        bool ExecuteFirstImmediately = false);

    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    [ProducesResponseType(typeof(RecurringTransactionsOverviewDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RecurringTransactionsOverviewDto>> GetRecurringTransactions(
        [FromRoute] Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new GetRecurringTransactionsQuery(workspaceId), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Contributor)]
    [ProducesResponseType(typeof(RecurringTransactionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecurringTransactionDto>> CreateRecurringTransaction(
        [FromRoute] Guid workspaceId,
        [FromBody] CreateRecurringTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _sender.Send(
            new CreateRecurringTransactionCommand(
                workspaceId,
                request.WalletId,
                request.CategoryId,
                request.Type,
                request.Amount,
                request.Title,
                request.DayOfMonth,
                request.TotalOccurrences,
                request.ExecuteFirstImmediately),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPatch("{id:guid}/toggle")]
    [RequireWorkspaceRole(WorkspaceRole.Contributor)]
    [ProducesResponseType(typeof(RecurringTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RecurringTransactionDto>> ToggleRecurringTransaction(
        [FromRoute] Guid workspaceId,
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var updated = await _sender.Send(
            new ToggleRecurringTransactionCommand(workspaceId, id),
            cancellationToken);

        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Contributor)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRecurringTransaction(
        [FromRoute] Guid workspaceId,
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteRecurringTransactionCommand(workspaceId, id), cancellationToken);
        return NoContent();
    }
}
