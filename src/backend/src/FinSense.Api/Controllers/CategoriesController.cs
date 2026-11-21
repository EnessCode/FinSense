using FinSense.Api.Filters;
using FinSense.Application.Features.Categories.Commands;
using FinSense.Application.Features.Categories.Contracts;
using FinSense.Application.Features.Categories.Queries.GetCategories;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinSense.Api.Controllers;

[ApiController]
[Route("api/v1/workspaces/{wsId:guid}/categories")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly ISender _sender;

    public CategoriesController(ISender sender)
    {
        _sender = sender;
    }

    public sealed record CreateCategoryRequest(
        string NameTr,
        string? NameEn,
        CategoryType Type,
        string Icon,
        string ColorHex);

    public sealed record UpdateCategoryRequest(
        string NameTr,
        string? NameEn,
        string Icon,
        string ColorHex);

    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetCategories(
        [FromRoute] Guid wsId,
        [FromQuery] CategoryType? type = null,
        CancellationToken cancellationToken = default)
    {
        var categories = await _sender.Send(new GetCategoriesQuery(wsId, type), cancellationToken);
        return Ok(categories);
    }

    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryDto>> CreateCategory(
        [FromRoute] Guid wsId,
        [FromBody] CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _sender.Send(
            new CreateCategoryCommand(
                wsId,
                request.NameTr,
                request.NameEn,
                request.Type,
                request.Icon,
                request.ColorHex),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryDto>> UpdateCategory(
        [FromRoute] Guid wsId,
        [FromRoute] Guid id,
        [FromBody] UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _sender.Send(
            new UpdateCategoryCommand(
                wsId,
                id,
                request.NameTr,
                request.NameEn,
                request.Icon,
                request.ColorHex),
            cancellationToken);

        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCategory(
        [FromRoute] Guid wsId,
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteCategoryCommand(wsId, id), cancellationToken);
        return NoContent();
    }
}
