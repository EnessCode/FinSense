using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.Wallets.Contracts;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.Wallets.Queries.GetWallets;

public sealed record GetWalletsQuery(Guid WorkspaceId, bool IncludeArchived = true) : IRequest<IReadOnlyList<WalletDto>>;

public sealed class GetWalletsQueryHandler : IRequestHandler<GetWalletsQuery, IReadOnlyList<WalletDto>>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;

    public GetWalletsQueryHandler(IAppDbContext dbContext, IWorkspaceAccessService workspaceAccess)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
    }

    public async Task<IReadOnlyList<WalletDto>> Handle(GetWalletsQuery request, CancellationToken cancellationToken)
    {
        await _workspaceAccess.EnsureAccessAsync(request.WorkspaceId, WorkspaceRole.Viewer, cancellationToken);

        var query = _dbContext.Wallets
            .AsNoTracking()
            .Where(w => w.WorkspaceId == request.WorkspaceId && !w.IsDeleted);

        if (!request.IncludeArchived)
        {
            query = query.Where(w => !w.IsArchived);
        }

        return await query
            .OrderBy(w => w.IsArchived)
            .ThenBy(w => w.Type)
            .ThenBy(w => w.Name)
            .Select(WalletMappings.Projection)
            .ToListAsync(cancellationToken);
    }
}
