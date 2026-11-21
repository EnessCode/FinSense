using FinSense.Application.Common.Auditing;
using FinSense.Application.Common.Exceptions;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Categories.Contracts;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Categories.Commands;

internal static class CategoryRules
{
    public const string ColorHexPattern = "^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$";

    public static async Task EnsureNameIsUniqueAsync(
        IAppDbContext dbContext,
        Guid workspaceId,
        CategoryType type,
        string nameTr,
        Guid? excludeCategoryId,
        CancellationToken cancellationToken)
    {
        var normalized = nameTr.Trim().ToLower();

        var exists = await dbContext.Categories
            .AsNoTracking()
            .AnyAsync(
                c => c.WorkspaceId == workspaceId &&
                     !c.IsDeleted &&
                     c.Type == type &&
                     c.Id != excludeCategoryId &&
                     c.NameTr.ToLower() == normalized,
                cancellationToken);

        if (exists)
        {
            throw new ConflictException(
                "CATEGORY_NAME_ALREADY_EXISTS",
                "Bu çalışma alanında aynı isimde ve türde bir kategori zaten mevcut.");
        }
    }

    public static CategoryDto ToDto(Category c, bool isEnglish) => new(
        c.Id,
        isEnglish && c.NameEn != null ? c.NameEn : c.NameTr,
        c.NameTr,
        c.NameEn,
        c.Type,
        c.Icon,
        c.ColorHex,
        c.IsSystem);
}

// ───────────────────────────── Create ─────────────────────────────

public sealed record CreateCategoryCommand(
    Guid WorkspaceId,
    string NameTr,
    string? NameEn,
    CategoryType Type,
    string Icon,
    string ColorHex) : IRequest<CategoryDto>;

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.NameTr)
            .NotEmpty().WithMessage("Kategori adı (Türkçe) zorunludur.")
            .MaximumLength(100);
        RuleFor(x => x.NameEn).MaximumLength(100);
        RuleFor(x => x.Type).IsInEnum().WithMessage("Geçersiz kategori türü.");
        RuleFor(x => x.Icon).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ColorHex)
            .NotEmpty()
            .Matches(CategoryRules.ColorHexPattern).WithMessage("Renk kodu #RRGGBB veya #RRGGBBAA formatında olmalıdır.");
    }
}

public sealed class CreateCategoryCommandHandler : IRequestHandler<CreateCategoryCommand, CategoryDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateCategoryCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<CategoryDto> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(request.WorkspaceId, WorkspaceRole.Admin, cancellationToken);

        await CategoryRules.EnsureNameIsUniqueAsync(
            _dbContext, request.WorkspaceId, request.Type, request.NameTr, null, cancellationToken);

        var category = Category.CreateCustom(
            request.WorkspaceId, request.NameTr, request.NameEn, request.Type, request.Icon, request.ColorHex);

        _dbContext.Categories.Add(category);

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId, access.UserId, "CategoryCreated", nameof(Category), category.Id,
                null, AuditJson.Serialize(new { category.NameTr, category.NameEn, category.Type }),
                _dateTimeProvider.UtcNow));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return CategoryRules.ToDto(category, CategoryLocalization.IsEnglish(_currentUserService.Locale));
    }
}

// ───────────────────────────── Update ─────────────────────────────

public sealed record UpdateCategoryCommand(
    Guid WorkspaceId,
    Guid CategoryId,
    string NameTr,
    string? NameEn,
    string Icon,
    string ColorHex) : IRequest<CategoryDto>;

public sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.NameTr)
            .NotEmpty().WithMessage("Kategori adı (Türkçe) zorunludur.")
            .MaximumLength(100);
        RuleFor(x => x.NameEn).MaximumLength(100);
        RuleFor(x => x.Icon).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ColorHex)
            .NotEmpty()
            .Matches(CategoryRules.ColorHexPattern).WithMessage("Renk kodu #RRGGBB veya #RRGGBBAA formatında olmalıdır.");
    }
}

public sealed class UpdateCategoryCommandHandler : IRequestHandler<UpdateCategoryCommand, CategoryDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateCategoryCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<CategoryDto> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(request.WorkspaceId, WorkspaceRole.Admin, cancellationToken);

        var category = await _dbContext.Categories
            .FirstOrDefaultAsync(
                c => c.Id == request.CategoryId && c.WorkspaceId == request.WorkspaceId && !c.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException("CATEGORY_NOT_FOUND", "Kategori bu çalışma alanında bulunamadı.");

        var before = new { category.NameTr, category.NameEn, category.Icon, category.ColorHex };

        // Domain guard throws SYSTEM_CATEGORY_IMMUTABLE (400) for is_system = true before any mutation.
        category.Update(request.NameTr, request.NameEn, request.Icon, request.ColorHex);

        await CategoryRules.EnsureNameIsUniqueAsync(
            _dbContext, request.WorkspaceId, category.Type, category.NameTr, category.Id, cancellationToken);

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId, access.UserId, "CategoryUpdated", nameof(Category), category.Id,
                AuditJson.Serialize(before),
                AuditJson.Serialize(new { category.NameTr, category.NameEn, category.Icon, category.ColorHex }),
                _dateTimeProvider.UtcNow));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return CategoryRules.ToDto(category, CategoryLocalization.IsEnglish(_currentUserService.Locale));
    }
}

// ───────────────────────────── Delete ─────────────────────────────

public sealed record DeleteCategoryCommand(Guid WorkspaceId, Guid CategoryId) : IRequest;

public sealed class DeleteCategoryCommandHandler : IRequestHandler<DeleteCategoryCommand>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly IDateTimeProvider _dateTimeProvider;

    public DeleteCategoryCommandHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        var access = await _workspaceAccess.EnsureAccessAsync(request.WorkspaceId, WorkspaceRole.Admin, cancellationToken);

        var category = await _dbContext.Categories
            .FirstOrDefaultAsync(
                c => c.Id == request.CategoryId && c.WorkspaceId == request.WorkspaceId && !c.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException("CATEGORY_NOT_FOUND", "Kategori bu çalışma alanında bulunamadı.");

        // Domain guard: system categories cannot be deleted. Custom categories are soft-deleted so
        // historical transactions keep their category reference and analytics remain intact.
        category.SoftDelete();

        // Active budgets targeting a now-hidden category would be unreachable; retire them alongside it.
        var orphanedBudgets = await _dbContext.Budgets
            .Where(b => b.WorkspaceId == request.WorkspaceId && b.CategoryId == category.Id && !b.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var budget in orphanedBudgets)
        {
            budget.SoftDelete();
        }

        if (access.IsHousehold)
        {
            _dbContext.AuditLogs.Add(AuditLog.Create(
                request.WorkspaceId, access.UserId, "CategoryDeleted", nameof(Category), category.Id,
                AuditJson.Serialize(new { category.NameTr, category.Type, RetiredBudgetCount = orphanedBudgets.Count }),
                null,
                _dateTimeProvider.UtcNow));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
