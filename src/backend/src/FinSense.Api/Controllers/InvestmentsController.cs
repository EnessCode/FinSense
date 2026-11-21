using FinSense.Api.Filters;
using FinSense.Application.Features.Investments.Commands;
using FinSense.Application.Features.Investments.Contracts;
using FinSense.Application.Features.Investments.Queries.GetInvestmentPortfolio;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinSense.Api.Controllers;

[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/investments")]
[Authorize]
public class InvestmentsController : ControllerBase
{
    private readonly ISender _sender;

    public InvestmentsController(ISender sender)
    {
        _sender = sender;
    }

    public sealed record AddInvestmentLotRequest(
        InvestmentAssetCategory Category,
        string Symbol,
        string AssetName,
        decimal Quantity,
        decimal UnitPrice,
        decimal? TotalCost = null,
        decimal? CurrentUnitPrice = null,
        DateTime? PurchaseDateUtc = null,
        string? Notes = null);

    public sealed record UpdateAssetCurrentPriceRequest(
        string Symbol,
        decimal CurrentUnitPrice);

    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    [ProducesResponseType(typeof(InvestmentPortfolioDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<InvestmentPortfolioDto>> GetPortfolio(
        [FromRoute] Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var portfolio = await _sender.Send(new GetInvestmentPortfolioQuery(workspaceId), cancellationToken);
        return Ok(portfolio);
    }

    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Contributor)]
    [ProducesResponseType(typeof(InvestmentLotDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<InvestmentLotDto>> AddLot(
        [FromRoute] Guid workspaceId,
        [FromBody] AddInvestmentLotRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _sender.Send(
            new AddInvestmentLotCommand(
                workspaceId,
                request.Category,
                request.Symbol,
                request.AssetName,
                request.Quantity,
                request.UnitPrice,
                request.TotalCost,
                request.CurrentUnitPrice,
                request.PurchaseDateUtc,
                request.Notes),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPatch("price")]
    [RequireWorkspaceRole(WorkspaceRole.Contributor)]
    [ProducesResponseType(typeof(UpdateAssetPriceResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UpdateAssetPriceResultDto>> UpdateCurrentPrice(
        [FromRoute] Guid workspaceId,
        [FromBody] UpdateAssetCurrentPriceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new UpdateAssetCurrentPriceCommand(workspaceId, request.Symbol, request.CurrentUnitPrice),
            cancellationToken);

        return Ok(result);
    }

    [HttpDelete("{lotId:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Contributor)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteLot(
        [FromRoute] Guid workspaceId,
        [FromRoute] Guid lotId,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteInvestmentLotCommand(workspaceId, lotId), cancellationToken);
        return NoContent();
    }
}
