using System.Linq.Expressions;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;

namespace FinSense.Application.Features.Wallets.Contracts;

public sealed record WalletDto(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    WalletType Type,
    string CurrencyCode,
    decimal InitialBalance,
    decimal CurrentBalance,
    decimal? CreditLimit,
    decimal? AvailableCredit,
    string? Icon,
    string? ColorHex,
    bool IsArchived);

public static class WalletMappings
{
    /// <summary>EF-translatable projection (used with AsNoTracking queries).</summary>
    public static readonly Expression<Func<Wallet, WalletDto>> Projection = w => new WalletDto(
        w.Id,
        w.WorkspaceId,
        w.Name,
        w.Type,
        w.CurrencyCode,
        w.InitialBalance,
        w.CurrentBalance,
        w.CreditLimit,
        w.Type == WalletType.CreditCard && w.CreditLimit != null ? w.CreditLimit + w.CurrentBalance : null,
        w.Icon,
        w.ColorHex,
        w.IsArchived);

    private static readonly Func<Wallet, WalletDto> CompiledProjection = Projection.Compile();

    public static WalletDto ToDto(Wallet wallet) => CompiledProjection(wallet);
}
