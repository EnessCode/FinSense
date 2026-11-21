using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Categories.Contracts;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Categories.Queries.GetCategories;

public sealed record GetCategoriesQuery(Guid WorkspaceId, CategoryType? Type = null) : IRequest<IReadOnlyList<CategoryDto>>;

public sealed class GetCategoriesQueryHandler : IRequestHandler<GetCategoriesQuery, IReadOnlyList<CategoryDto>>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;
    private readonly ICurrentUserService _currentUserService;

    public GetCategoriesQueryHandler(
        IAppDbContext dbContext,
        IWorkspaceAccessService workspaceAccess,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
        _currentUserService = currentUserService;
    }

    public async Task<IReadOnlyList<CategoryDto>> Handle(GetCategoriesQuery request, CancellationToken cancellationToken)
    {
        await _workspaceAccess.EnsureAccessAsync(request.WorkspaceId, WorkspaceRole.Viewer, cancellationToken);
        var isEnglish = CategoryLocalization.IsEnglish(_currentUserService.Locale);

        var query = _dbContext.Categories
            .AsNoTracking()
            .Where(c => c.WorkspaceId == request.WorkspaceId && !c.IsDeleted);

        if (request.Type.HasValue)
        {
            query = query.Where(c => c.Type == request.Type.Value);
        }

        return await query
            .OrderBy(c => c.Type)
            .ThenByDescending(c => c.IsSystem)
            .ThenBy(c => c.NameTr)
            .Select(c => new CategoryDto(
                c.Id,
                isEnglish && c.NameEn != null ? c.NameEn : c.NameTr,
                c.NameTr,
                c.NameEn,
                c.Type,
                c.Icon,
                c.ColorHex,
                c.IsSystem))
            .ToListAsync(cancellationToken);
    }
}
