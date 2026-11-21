using FinSense.Api.Filters;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Features.Analytics.Contracts;
using FinSense.Application.Features.Analytics.Queries.GetAnalyticsSummary;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinSense.Api.Controllers;

[ApiController]
[Route("api/v1/workspaces/{wsId:guid}/analytics")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IDateTimeProvider _dateTimeProvider;

    public AnalyticsController(ISender sender, IDateTimeProvider dateTimeProvider)
    {
        _sender = sender;
        _dateTimeProvider = dateTimeProvider;
    }

    [HttpGet("summary")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    [ProducesResponseType(typeof(AnalyticsSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AnalyticsSummaryDto>> GetSummary(
        [FromRoute] Guid wsId,
        [FromQuery] DateOnly? dateFrom = null,
        [FromQuery] DateOnly? dateTo = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        CancellationToken cancellationToken = default)
    {
        var today = _dateTimeProvider.TodayIstanbul;
        DateOnly resolvedStart;
        DateOnly resolvedEnd;

        if (dateFrom.HasValue || dateTo.HasValue)
        {
            var defaultStart = new DateOnly(today.Year, today.Month, 1);
            var defaultEnd = defaultStart.AddMonths(1).AddDays(-1);
            resolvedStart = dateFrom ?? defaultStart;
            resolvedEnd = dateTo ?? defaultEnd;
        }
        else if (year.HasValue || month.HasValue)
        {
            var targetYear = year ?? today.Year;
            var targetMonth = month ?? today.Month;
            if (targetYear < 2000 || targetYear > 2100 || targetMonth < 1 || targetMonth > 12)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Geçersiz dönem parametresi.",
                    Detail = "Yıl 2000-2100 ve ay 1-12 aralığında olmalıdır.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            resolvedStart = new DateOnly(targetYear, targetMonth, 1);
            resolvedEnd = resolvedStart.AddMonths(1).AddDays(-1);
        }
        else
        {
            resolvedStart = new DateOnly(today.Year, today.Month, 1);
            resolvedEnd = resolvedStart.AddMonths(1).AddDays(-1);
        }

        var summary = await _sender.Send(
            new GetAnalyticsSummaryQuery(
                wsId,
                resolvedStart,
                resolvedEnd),
            cancellationToken);

        return Ok(summary);
    }
}
