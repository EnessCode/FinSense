using FinSense.Domain.Exceptions;

namespace FinSense.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public Guid TokenFamilyId { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }

    public User User { get; private set; } = null!;

    private RefreshToken() { }

    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        DateTime expiresAtUtc,
        Guid? tokenFamilyId = null)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("INVALID_USER_ID", "Geçerli bir kullanıcı kimliği gereklidir.");
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainException("TOKEN_HASH_REQUIRED", "Yenileme anahtarı özeti zorunludur.");
        }

        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash.Trim(),
            TokenFamilyId = tokenFamilyId ?? Guid.NewGuid(),
            ExpiresAtUtc = expiresAtUtc
        };
    }

    public bool IsExpired(DateTime utcNow) => utcNow >= ExpiresAtUtc;

    public bool IsRevoked => RevokedAtUtc.HasValue;

    public bool IsActive(DateTime utcNow) => !IsRevoked && !IsExpired(utcNow);

    public void Revoke(DateTime utcNow, string? replacedByTokenHash = null)
    {
        if (!RevokedAtUtc.HasValue)
        {
            RevokedAtUtc = utcNow;
        }

        if (!string.IsNullOrWhiteSpace(replacedByTokenHash))
        {
            ReplacedByTokenHash = replacedByTokenHash.Trim();
        }
    }
}
