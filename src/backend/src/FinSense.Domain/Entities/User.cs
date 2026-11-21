using FinSense.Domain.Exceptions;

namespace FinSense.Domain.Entities;

public class User
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string PreferredLocale { get; private set; } = "tr-TR";
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private readonly List<RefreshToken> _refreshTokens = [];
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    private readonly List<WorkspaceMember> _workspaceMemberships = [];
    public IReadOnlyCollection<WorkspaceMember> WorkspaceMemberships => _workspaceMemberships.AsReadOnly();

    private User() { }

    public static User Create(
        string email,
        string passwordHash,
        string displayName,
        string preferredLocale = "tr-TR",
        DateTime? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException("USER_EMAIL_REQUIRED", "E-posta adresi zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("USER_PASSWORD_HASH_REQUIRED", "Şifre özeti zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException("USER_DISPLAY_NAME_REQUIRED", "Görünen ad zorunludur.");
        }

        var now = utcNow ?? DateTime.UtcNow;
        var normalizedLocale = string.IsNullOrWhiteSpace(preferredLocale) ? "tr-TR" : preferredLocale.Trim();

        return new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            DisplayName = displayName.Trim(),
            PreferredLocale = normalizedLocale,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public void UpdateProfile(string displayName, string preferredLocale, DateTime? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException("USER_DISPLAY_NAME_REQUIRED", "Görünen ad zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(preferredLocale))
        {
            throw new DomainException("USER_LOCALE_REQUIRED", "Dil tercihi zorunludur.");
        }

        DisplayName = displayName.Trim();
        PreferredLocale = preferredLocale.Trim();
        UpdatedAtUtc = utcNow ?? DateTime.UtcNow;
    }
}
