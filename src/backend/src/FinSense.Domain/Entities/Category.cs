using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;

namespace FinSense.Domain.Entities;

public class Category
{
    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public string NameTr { get; private set; } = string.Empty;
    public string? NameEn { get; private set; }
    public CategoryType Type { get; private set; }
    public string Icon { get; private set; } = string.Empty;
    public string ColorHex { get; private set; } = string.Empty;
    public bool IsSystem { get; private set; }
    public bool IsDeleted { get; private set; }

    public Workspace Workspace { get; private set; } = null!;

    private Category() { }

    public static Category CreateSystem(
        Guid workspaceId,
        string nameTr,
        string? nameEn,
        CategoryType type,
        string icon,
        string colorHex)
    {
        return Create(workspaceId, nameTr, nameEn, type, icon, colorHex, isSystem: true);
    }

    public static Category CreateCustom(
        Guid workspaceId,
        string nameTr,
        string? nameEn,
        CategoryType type,
        string icon,
        string colorHex)
    {
        return Create(workspaceId, nameTr, nameEn, type, icon, colorHex, isSystem: false);
    }

    private static Category Create(
        Guid workspaceId,
        string nameTr,
        string? nameEn,
        CategoryType type,
        string icon,
        string colorHex,
        bool isSystem)
    {
        if (workspaceId == Guid.Empty)
        {
            throw new DomainException("INVALID_WORKSPACE_ID", "Geçerli bir çalışma alanı kimliği gereklidir.");
        }

        if (string.IsNullOrWhiteSpace(nameTr))
        {
            throw new DomainException("CATEGORY_NAME_REQUIRED", "Kategori adı (Türkçe) zorunludur.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new DomainException("INVALID_CATEGORY_TYPE", "Geçersiz kategori türü.");
        }

        if (string.IsNullOrWhiteSpace(icon))
        {
            throw new DomainException("CATEGORY_ICON_REQUIRED", "Kategori ikonu zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(colorHex))
        {
            throw new DomainException("CATEGORY_COLOR_REQUIRED", "Kategori renk kodu zorunludur.");
        }

        return new Category
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            NameTr = nameTr.Trim(),
            NameEn = string.IsNullOrWhiteSpace(nameEn) ? null : nameEn.Trim(),
            Type = type,
            Icon = icon.Trim(),
            ColorHex = colorHex.Trim(),
            IsSystem = isSystem,
            IsDeleted = false
        };
    }

    public void Update(string nameTr, string? nameEn, string icon, string colorHex)
    {
        EnsureMutable();

        if (string.IsNullOrWhiteSpace(nameTr))
        {
            throw new DomainException("CATEGORY_NAME_REQUIRED", "Kategori adı (Türkçe) zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(icon))
        {
            throw new DomainException("CATEGORY_ICON_REQUIRED", "Kategori ikonu zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(colorHex))
        {
            throw new DomainException("CATEGORY_COLOR_REQUIRED", "Kategori renk kodu zorunludur.");
        }

        NameTr = nameTr.Trim();
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? null : nameEn.Trim();
        Icon = icon.Trim();
        ColorHex = colorHex.Trim();
    }

    public void SoftDelete()
    {
        EnsureMutable();
        IsDeleted = true;
    }

    private void EnsureMutable()
    {
        if (IsSystem)
        {
            throw new DomainException(
                "SYSTEM_CATEGORY_IMMUTABLE",
                "Sistem kategorileri değiştirilemez veya silinemez.");
        }

        if (IsDeleted)
        {
            throw new DomainException(
                "CATEGORY_ALREADY_DELETED",
                "Silinmiş kategori üzerinde işlem yapılamaz.");
        }
    }
}
