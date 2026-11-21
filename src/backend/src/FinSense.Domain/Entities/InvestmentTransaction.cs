using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;

namespace FinSense.Domain.Entities;

public class InvestmentTransaction
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public InvestmentAssetCategory Category { get; private set; }
    public string Symbol { get; private set; } = string.Empty;
    public string AssetName { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal TotalCost { get; private set; }
    public decimal CurrentUnitPrice { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public DateTime PurchaseDateUtc { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public Workspace Workspace { get; private set; } = null!;
    public User CreatedByUser { get; private set; } = null!;

    private InvestmentTransaction() { }

    public static InvestmentTransaction Create(
        Guid workspaceId,
        Guid createdByUserId,
        InvestmentAssetCategory category,
        string symbol,
        string assetName,
        decimal quantity,
        decimal unitPrice,
        decimal? totalCost = null,
        decimal? currentUnitPrice = null,
        DateTime? purchaseDateUtc = null,
        string? notes = null,
        string currency = "TRY",
        DateTime? utcNow = null)
    {
        if (workspaceId == Guid.Empty)
        {
            throw new DomainException("INVALID_WORKSPACE_ID", "Geçerli bir çalışma alanı kimliği gereklidir.");
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new DomainException("INVALID_USER_ID", "Geçerli bir kullanıcı kimliği gereklidir.");
        }

        if (!Enum.IsDefined(category))
        {
            throw new DomainException("INVALID_INVESTMENT_CATEGORY", "Geçersiz yatırım varlık kategorisi.");
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new DomainException("INVESTMENT_SYMBOL_REQUIRED", "Varlık sembolü zorunludur.");
        }

        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        if (normalizedSymbol.Length > 24)
        {
            throw new DomainException("INVESTMENT_SYMBOL_TOO_LONG", "Varlık sembolü en fazla 24 karakter olabilir.");
        }

        if (string.IsNullOrWhiteSpace(assetName))
        {
            throw new DomainException("INVESTMENT_ASSET_NAME_REQUIRED", "Varlık adı zorunludur.");
        }

        var trimmedAssetName = assetName.Trim();
        if (trimmedAssetName.Length > 100)
        {
            throw new DomainException("INVESTMENT_ASSET_NAME_TOO_LONG", "Varlık adı en fazla 100 karakter olabilir.");
        }

        if (quantity <= 0m)
        {
            throw new DomainException("INVALID_INVESTMENT_QUANTITY", "Varlık miktarı sıfırdan büyük olmalıdır.");
        }

        if (unitPrice <= 0m)
        {
            throw new DomainException("INVALID_INVESTMENT_UNIT_PRICE", "Birim alış fiyatı sıfırdan büyük olmalıdır.");
        }

        if (totalCost.HasValue && totalCost.Value < 0m)
        {
            throw new DomainException("INVALID_INVESTMENT_TOTAL_COST", "Toplam maliyet negatif olamaz.");
        }

        if (currentUnitPrice.HasValue && currentUnitPrice.Value <= 0m)
        {
            throw new DomainException("INVALID_CURRENT_UNIT_PRICE", "Güncel birim fiyat sıfırdan büyük olmalıdır.");
        }

        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (trimmedNotes is not null && trimmedNotes.Length > 250)
        {
            throw new DomainException("INVESTMENT_NOTES_TOO_LONG", "Not alanı en fazla 250 karakter olabilir.");
        }

        var roundedQuantity = decimal.Round(quantity, 6, MidpointRounding.AwayFromZero);
        var roundedUnitPrice = decimal.Round(unitPrice, 4, MidpointRounding.AwayFromZero);

        var resolvedTotalCost = (!totalCost.HasValue || totalCost.Value == 0m)
            ? decimal.Round(roundedQuantity * roundedUnitPrice, 2, MidpointRounding.AwayFromZero)
            : decimal.Round(totalCost.Value, 2, MidpointRounding.AwayFromZero);

        if (resolvedTotalCost <= 0m)
        {
            throw new DomainException("INVALID_INVESTMENT_TOTAL_COST", "Toplam alış maliyeti sıfırdan büyük olmalıdır.");
        }

        var resolvedCurrentPrice = currentUnitPrice.HasValue
            ? decimal.Round(currentUnitPrice.Value, 4, MidpointRounding.AwayFromZero)
            : roundedUnitPrice;

        var now = utcNow ?? DateTime.UtcNow;
        var resolvedPurchaseDate = NormalizeUtc(purchaseDateUtc ?? now);

        return new InvestmentTransaction
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            CreatedByUserId = createdByUserId,
            Category = category,
            Symbol = normalizedSymbol,
            AssetName = trimmedAssetName,
            Quantity = roundedQuantity,
            UnitPrice = roundedUnitPrice,
            TotalCost = resolvedTotalCost,
            CurrentUnitPrice = resolvedCurrentPrice,
            Currency = Workspace.NormalizeCurrencyCode(currency),
            PurchaseDateUtc = resolvedPurchaseDate,
            Notes = trimmedNotes,
            CreatedAtUtc = now
        };
    }

    public void UpdateCurrentUnitPrice(decimal newUnitPrice)
    {
        if (newUnitPrice <= 0m)
        {
            throw new DomainException("INVALID_CURRENT_UNIT_PRICE", "Güncel birim fiyat sıfırdan büyük olmalıdır.");
        }

        CurrentUnitPrice = decimal.Round(newUnitPrice, 4, MidpointRounding.AwayFromZero);
    }

    private static DateTime NormalizeUtc(DateTime dt) => dt.Kind switch
    {
        DateTimeKind.Utc => dt,
        DateTimeKind.Local => dt.ToUniversalTime(),
        _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
    };
}
