using FinSense.Api.Filters;
using FinSense.Application.Features.Wallets.Commands.CreateWallet;
using FinSense.Application.Features.Wallets.Commands.DeleteWallet;
using FinSense.Application.Features.Wallets.Commands.UpdateWallet;
using FinSense.Application.Features.Wallets.Contracts;
using FinSense.Application.Features.Wallets.Queries.GetWallets;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinSense.Api.Controllers;

[ApiController]
[Route("api/v1/workspaces/{wsId:guid}/wallets")]
[Authorize]
public class WalletsController : ControllerBase
{
    private readonly ISender _sender;

    public WalletsController(ISender sender)
    {
        _sender = sender;
    }

    public sealed record CreateWalletRequest(
        string Name,
        WalletType Type,
        decimal InitialBalance = 0m,
        string? CurrencyCode = null,
        decimal? CreditLimit = null,
        string? Icon = null,
        string? ColorHex = null);

    public sealed record UpdateWalletRequest(
        string Name,
        decimal? CreditLimit = null,
        string? Icon = null,
        string? ColorHex = null,
        bool IsArchived = false,
        decimal? InitialBalance = null);

    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    [ProducesResponseType(typeof(IReadOnlyList<WalletDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WalletDto>>> GetWallets(
        [FromRoute] Guid wsId,
        [FromQuery] bool includeArchived = true,
        CancellationToken cancellationToken = default)
    {
        var wallets = await _sender.Send(new GetWalletsQuery(wsId, includeArchived), cancellationToken);
        return Ok(wallets);
    }

    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(WalletDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WalletDto>> CreateWallet(
        [FromRoute] Guid wsId,
        [FromBody] CreateWalletRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _sender.Send(
            new CreateWalletCommand(
                wsId,
                request.Name,
                request.Type,
                request.InitialBalance,
                request.CurrencyCode,
                request.CreditLimit,
                request.Icon,
                request.ColorHex),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(WalletDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WalletDto>> UpdateWallet(
        [FromRoute] Guid wsId,
        [FromRoute] Guid id,
        [FromBody] UpdateWalletRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _sender.Send(
            new UpdateWalletCommand(
                wsId,
                id,
                request.Name,
                request.CreditLimit,
                request.Icon,
                request.ColorHex,
                request.IsArchived,
                request.InitialBalance),
            cancellationToken);

        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteWallet(
        [FromRoute] Guid wsId,
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteWalletCommand(wsId, id), cancellationToken);
        return NoContent();
    }
}
