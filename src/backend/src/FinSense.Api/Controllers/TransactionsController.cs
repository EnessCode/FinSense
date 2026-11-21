using System.Text;
using FinSense.Api.Filters;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Models;
using FinSense.Application.Features.Transactions.Commands;
using FinSense.Application.Features.Transactions.Contracts;
using FinSense.Application.Features.Transactions.Import;
using FinSense.Application.Features.Transactions.Queries.GetTransactionById;
using FinSense.Application.Features.Transactions.Queries.GetTransactions;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinSense.Api.Controllers;

[ApiController]
[Route("api/v1/workspaces/{wsId:guid}/transactions")]
[Authorize]
public class TransactionsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IDateTimeProvider _dateTimeProvider;

    public TransactionsController(ISender sender, IDateTimeProvider dateTimeProvider)
    {
        _sender = sender;
        _dateTimeProvider = dateTimeProvider;
    }

    public sealed record CreateTransactionRequest(
        TransactionType Type,
        Guid WalletId,
        Guid? CounterpartyWalletId,
        Guid? CategoryId,
        Guid? ReceiptId,
        decimal Amount,
        string? CurrencyCode,
        DateOnly TransactionDate,
        string? MerchantName,
        string? Notes,
        Guid? PaidByUserId,
        string? IdempotencyKey = null,
        int? InstallmentCount = null);

    public sealed record UpdateTransactionRequest(
        TransactionType Type,
        Guid WalletId,
        Guid? CounterpartyWalletId,
        Guid? CategoryId,
        decimal Amount,
        DateOnly TransactionDate,
        string? MerchantName,
        string? Notes,
        Guid? PaidByUserId);

    public sealed record ConfirmStatementImportRequest(
        Guid WalletId,
        IReadOnlyList<ConfirmImportItemInput> Items);

    [HttpGet]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    [ProducesResponseType(typeof(PaginatedList<TransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaginatedList<TransactionDto>>> GetTransactions(
        [FromRoute] Guid wsId,
        [FromQuery] DateOnly? dateFrom = null,
        [FromQuery] DateOnly? dateTo = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] Guid? walletId = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] TransactionType? type = null,
        [FromQuery] Guid? paidByUserId = null,
        [FromQuery] string? search = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var resolvedDateFrom = dateFrom;
        var resolvedDateTo = dateTo;

        if (!dateFrom.HasValue && !dateTo.HasValue && (year.HasValue || month.HasValue))
        {
            var today = _dateTimeProvider.TodayIstanbul;
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

            resolvedDateFrom = new DateOnly(targetYear, targetMonth, 1);
            resolvedDateTo = resolvedDateFrom.Value.AddMonths(1).AddDays(-1);
        }

        var page = await _sender.Send(
            new GetTransactionsQuery(
                wsId,
                resolvedDateFrom,
                resolvedDateTo,
                walletId,
                categoryId,
                type,
                paidByUserId,
                search,
                pageNumber,
                pageSize),
            cancellationToken);

        return Ok(page);
    }

    [HttpGet("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Viewer)]
    [ProducesResponseType(typeof(TransactionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionDetailDto>> GetTransactionById(
        [FromRoute] Guid wsId,
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var detail = await _sender.Send(new GetTransactionByIdQuery(wsId, id), cancellationToken);
        return Ok(detail);
    }

    [HttpPost]
    [RequireWorkspaceRole(WorkspaceRole.Contributor)]
    [ProducesResponseType(typeof(TransactionDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TransactionDetailDto>> CreateTransaction(
        [FromRoute] Guid wsId,
        [FromBody] CreateTransactionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? headerIdempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveIdempotencyKey = !string.IsNullOrWhiteSpace(headerIdempotencyKey)
            ? headerIdempotencyKey
            : request.IdempotencyKey;

        var created = await _sender.Send(
            new CreateTransactionCommand(
                wsId,
                request.Type,
                request.WalletId,
                request.CounterpartyWalletId,
                request.CategoryId,
                request.ReceiptId,
                request.Amount,
                string.IsNullOrWhiteSpace(request.CurrencyCode) ? "TRY" : request.CurrencyCode,
                request.TransactionDate,
                request.MerchantName,
                request.Notes,
                request.PaidByUserId,
                effectiveIdempotencyKey,
                request.InstallmentCount),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Contributor)]
    [ProducesResponseType(typeof(TransactionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionDetailDto>> UpdateTransaction(
        [FromRoute] Guid wsId,
        [FromRoute] Guid id,
        [FromBody] UpdateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _sender.Send(
            new UpdateTransactionCommand(
                wsId,
                id,
                request.Type,
                request.WalletId,
                request.CounterpartyWalletId,
                request.CategoryId,
                request.Amount,
                request.TransactionDate,
                request.MerchantName,
                request.Notes,
                request.PaidByUserId),
            cancellationToken);

        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [RequireWorkspaceRole(WorkspaceRole.Contributor)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTransaction(
        [FromRoute] Guid wsId,
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteTransactionCommand(wsId, id), cancellationToken);
        return NoContent();
    }

    [HttpPost("import/preview")]
    [RequireWorkspaceRole(WorkspaceRole.Contributor)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(PreviewStatementImportCommandHandler.MaxFileSizeBytes)]
    [ProducesResponseType(typeof(ImportPreviewResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ImportPreviewResultDto>> PreviewStatementImport(
        [FromRoute] Guid wsId,
        [FromForm] Guid walletId,
        IFormFile? file = null,
        [FromForm] string? rawCsvText = null,
        [FromForm] string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        byte[] content;
        string effectiveFileName;

        if (file is not null && file.Length > 0)
        {
            if (file.Length > PreviewStatementImportCommandHandler.MaxFileSizeBytes)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Dosya boyutu çok büyük.",
                    Detail = "Ekstre dosyası en fazla 10 MB olabilir.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, cancellationToken);
            content = ms.ToArray();
            effectiveFileName = file.FileName;
        }
        else if (!string.IsNullOrWhiteSpace(rawCsvText))
        {
            content = Encoding.UTF8.GetBytes(rawCsvText);
            effectiveFileName = string.IsNullOrWhiteSpace(fileName) ? "ornek-ekstre.csv" : fileName.Trim();
        }
        else
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Ekstre dosyası eksik.",
                Detail = "Lütfen bir PDF, Excel (.xlsx) veya CSV ekstre dosyası yükleyin.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var preview = await _sender.Send(
            new PreviewStatementImportCommand(wsId, walletId, effectiveFileName, content, file?.ContentType),
            cancellationToken);

        return Ok(preview);
    }

    [HttpPost("import/confirm")]
    [RequireWorkspaceRole(WorkspaceRole.Contributor)]
    [ProducesResponseType(typeof(ConfirmImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConfirmImportResultDto>> ConfirmStatementImport(
        [FromRoute] Guid wsId,
        [FromBody] ConfirmStatementImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(
            new ConfirmStatementImportCommand(wsId, request.WalletId, request.Items),
            cancellationToken);

        return Ok(result);
    }
}
