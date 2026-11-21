using FinSense.Application.Common.Interfaces;
using FinSense.Application.Common.Security;
using FinSense.Application.Features.SavingsGoals.Contracts;
using FinSense.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Features.SavingsGoals.Queries.GetSavingsGoals;

public sealed record GetSavingsGoalsQuery(Guid WorkspaceId) : IRequest<IReadOnlyList<SavingsGoalDto>>;

public sealed class GetSavingsGoalsQueryHandler : IRequestHandler<GetSavingsGoalsQuery, IReadOnlyList<SavingsGoalDto>>
{
    private readonly IAppDbContext _dbContext;
    private readonly IWorkspaceAccessService _workspaceAccess;

    public GetSavingsGoalsQueryHandler(IAppDbContext dbContext, IWorkspaceAccessService workspaceAccess)
    {
        _dbContext = dbContext;
        _workspaceAccess = workspaceAccess;
    }

    public async Task<IReadOnlyList<SavingsGoalDto>> Handle(
        GetSavingsGoalsQuery request,
        CancellationToken cancellationToken)
    {
        await _workspaceAccess.EnsureAccessAsync(request.WorkspaceId, WorkspaceRole.Viewer, cancellationToken);

        var rows = await _dbContext.SavingsGoals
            .AsNoTracking()
            .Where(sg => sg.WorkspaceId == request.WorkspaceId && !sg.IsDeleted)
            .OrderBy(sg => sg.TargetDate == null)
            .ThenBy(sg => sg.TargetDate)
            .ThenBy(sg => sg.Name)
            .Select(sg => new
            {
                sg.Id,
                sg.WorkspaceId,
                sg.LinkedWalletId,
                LinkedWalletName = sg.LinkedWallet != null && !sg.LinkedWallet.IsDeleted ? sg.LinkedWallet.Name : null,
                LinkedWalletBalance = sg.LinkedWallet != null && !sg.LinkedWallet.IsDeleted ? (decimal?)sg.LinkedWallet.CurrentBalance : null,
                sg.Name,
                sg.TargetAmount,
                sg.CurrentAmount,
                sg.CurrencyCode,
                sg.TargetDate
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row =>
        {
            var effectiveCurrent = row.LinkedWalletId.HasValue && row.LinkedWalletBalance.HasValue
                ? row.LinkedWalletBalance.Value
                : row.CurrentAmount;

            return SavingsGoalMapper.ToDto(
                row.Id,
                row.WorkspaceId,
                row.LinkedWalletId,
                row.LinkedWalletName,
                row.Name,
                row.TargetAmount,
                effectiveCurrent,
                row.CurrencyCode,
                row.TargetDate);
        }).ToList();
    }
}
