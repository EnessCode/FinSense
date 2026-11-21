using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;

namespace FinSense.Domain.Entities;

public class Receipt
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public string ObjectKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long FileSizeBytes { get; private set; }
    public ReceiptStatus Status { get; private set; }
    public string? ExtractedMerchant { get; private set; }
    public DateOnly? ExtractedDate { get; private set; }
    public decimal? ExtractedTotal { get; private set; }
    public decimal? ExtractedTax { get; private set; }
    public decimal? ConfidenceScore { get; private set; }
    public Guid? UploadedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public Workspace Workspace { get; private set; } = null!;
    public User? UploadedByUser { get; private set; }
    public Transaction? Transaction { get; private set; }

    private Receipt() { }

    public static Receipt CreatePending(
        Guid workspaceId,
        string objectKey,
        string contentType,
        long fileSizeBytes,
        Guid? uploadedByUserId,
        DateTime? utcNow = null)
    {
        if (workspaceId == Guid.Empty)
        {
            throw new DomainException("INVALID_WORKSPACE_ID", "Geçerli bir çalışma alanı kimliği gereklidir.");
        }

        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new DomainException("RECEIPT_OBJECT_KEY_REQUIRED", "Fiş depolama anahtarı (ObjectKey) zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new DomainException("RECEIPT_CONTENT_TYPE_REQUIRED", "Fiş dosya türü zorunludur.");
        }

        if (fileSizeBytes <= 0 || fileSizeBytes > 10 * 1024 * 1024)
        {
            throw new DomainException("INVALID_RECEIPT_FILE_SIZE", "Fiş dosya boyutu 0'dan büyük ve en fazla 10 MB olmalıdır.");
        }

        return new Receipt
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ObjectKey = objectKey.Trim(),
            ContentType = contentType.Trim().ToLowerInvariant(),
            FileSizeBytes = fileSizeBytes,
            Status = ReceiptStatus.PendingConfirmation,
            UploadedByUserId = uploadedByUserId,
            CreatedAtUtc = utcNow ?? DateTime.UtcNow
        };
    }

    public void ApplyOcrExtraction(
        string? merchantName,
        DateOnly? extractedDate,
        decimal? extractedTotal,
        decimal? extractedTax,
        decimal? confidenceScore)
    {
        if (confidenceScore.HasValue && (confidenceScore.Value < 0m || confidenceScore.Value > 1m))
        {
            throw new DomainException("INVALID_CONFIDENCE_SCORE", "OCR güven skoru 0.0 ile 1.0 arasında olmalıdır.");
        }

        ExtractedMerchant = string.IsNullOrWhiteSpace(merchantName) ? null : merchantName.Trim();
        ExtractedDate = extractedDate;
        ExtractedTotal = extractedTotal.HasValue
            ? decimal.Round(extractedTotal.Value, 4, MidpointRounding.AwayFromZero)
            : null;
        ExtractedTax = extractedTax.HasValue
            ? decimal.Round(extractedTax.Value, 4, MidpointRounding.AwayFromZero)
            : null;
        ConfidenceScore = confidenceScore.HasValue
            ? decimal.Round(confidenceScore.Value, 4, MidpointRounding.AwayFromZero)
            : null;
    }

    public void LinkToTransaction()
    {
        if (Status == ReceiptStatus.MarkedForDeletion)
        {
            throw new DomainException("RECEIPT_MARKED_FOR_DELETION", "Silinmek üzere işaretlenmiş fiş bir işleme bağlanamaz.");
        }

        Status = ReceiptStatus.LinkedToTransaction;
    }

    public void MarkForDeletion()
    {
        Status = ReceiptStatus.MarkedForDeletion;
    }
}
