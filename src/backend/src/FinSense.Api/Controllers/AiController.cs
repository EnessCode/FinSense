using System.Text.Json;
using FinSense.Api.Filters;
using FinSense.Application.Features.Ai.Commands.AskFinancialAssistant;
using FinSense.Application.Features.Ai.Commands.ClearAiChatHistory;
using FinSense.Application.Features.Ai.Commands.ScanReceipt;
using FinSense.Application.Features.Ai.Contracts;
using FinSense.Application.Features.Ai.Queries.GetAiChatHistory;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinSense.Api.Controllers;

[ApiController]
[Route("api/v1/workspaces/{workspaceId:guid}/ai")]
[Authorize]
public class AiController : ControllerBase
{
    private const long MaxReceiptFileSizeBytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ISender _sender;

    public AiController(ISender sender)
    {
        _sender = sender;
    }

    public sealed record AskFinancialAssistantRequest(string Question);

    public sealed record ScanReceiptJsonRequest(
        string? ImageBase64 = null,
        string? RawOcrText = null,
        string? FileName = null,
        string? MimeType = null);

    [HttpPost("chat")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    [ProducesResponseType(typeof(AiChatResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AiChatResponseDto>> Chat(
        [FromRoute] Guid workspaceId,
        [FromBody] AskFinancialAssistantRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _sender.Send(
            new AskFinancialAssistantCommand(workspaceId, request.Question),
            cancellationToken);

        return Ok(response);
    }

    [HttpGet("history")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    [ProducesResponseType(typeof(AiChatHistoryResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AiChatHistoryResponseDto>> GetHistory(
        [FromRoute] Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var history = await _sender.Send(new GetAiChatHistoryQuery(workspaceId), cancellationToken);
        return Ok(history);
    }

    [HttpDelete("history")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ClearHistory(
        [FromRoute] Guid workspaceId,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new ClearAiChatHistoryCommand(workspaceId), cancellationToken);
        return NoContent();
    }

    [HttpPost("scan-receipt")]
    [RequireWorkspaceRole(WorkspaceRole.Contributor)]
    [RequestSizeLimit(12 * 1024 * 1024)]
    [ProducesResponseType(typeof(ReceiptScanResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ReceiptScanResultDto>> ScanReceipt(
        [FromRoute] Guid workspaceId,
        CancellationToken cancellationToken)
    {
        string? imageBase64 = null;
        string? rawOcrText = null;
        string? fileName = null;
        string? mimeType = null;

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

            if (file is not null)
            {
                if (file.Length <= 0 || file.Length > MaxReceiptFileSizeBytes)
                {
                    throw new DomainException(
                        "INVALID_RECEIPT_FILE_SIZE",
                        "Fiş dosya boyutu 0'dan büyük ve en fazla 10 MB olmalıdır.");
                }

                await using var ms = new MemoryStream((int)file.Length);
                await file.CopyToAsync(ms, cancellationToken);
                imageBase64 = Convert.ToBase64String(ms.ToArray());
                fileName = file.FileName;
                mimeType = file.ContentType;
            }

            if (form.TryGetValue("rawOcrText", out var rawTextValues) &&
                !string.IsNullOrWhiteSpace(rawTextValues.ToString()))
            {
                rawOcrText = rawTextValues.ToString();
            }
        }
        else
        {
            ScanReceiptJsonRequest? payload;
            try
            {
                payload = await JsonSerializer.DeserializeAsync<ScanReceiptJsonRequest>(
                    Request.Body,
                    JsonOptions,
                    cancellationToken);
            }
            catch (JsonException)
            {
                throw new DomainException(
                    "INVALID_JSON_PAYLOAD",
                    "Geçersiz JSON istek gövdesi.");
            }

            if (payload is not null)
            {
                imageBase64 = payload.ImageBase64;
                rawOcrText = payload.RawOcrText;
                fileName = payload.FileName;
                mimeType = payload.MimeType;
            }
        }

        var result = await _sender.Send(
            new ScanReceiptCommand(workspaceId, imageBase64, rawOcrText, fileName, mimeType),
            cancellationToken);

        return Ok(result);
    }
}
