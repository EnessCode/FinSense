using FinSense.Application.Common.Auditing;
using FinSense.Application.Common.Interfaces;
using FinSense.Application.Features.Workspaces.Contracts;
using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Services;
using FluentValidation;
using MediatR;

namespace FinSense.Application.Features.Workspaces.Commands.CreateHouseholdWorkspace;

public record CreateHouseholdWorkspaceCommand(
    string Name,
    string DefaultCurrency = "TRY") : IRequest<WorkspaceSummaryDto>;

public class CreateHouseholdWorkspaceCommandValidator : AbstractValidator<CreateHouseholdWorkspaceCommand>
{
    public CreateHouseholdWorkspaceCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Çalışma alanı adı zorunludur.")
            .MaximumLength(100).WithMessage("Çalışma alanı adı en fazla 100 karakter olabilir.");

        RuleFor(x => x.DefaultCurrency)
            .NotEmpty().WithMessage("Para birimi kodu zorunludur.")
            .Length(3).WithMessage("Para birimi kodu 3 karakterli ISO 4217 formatında olmalıdır.");
    }
}

public class CreateHouseholdWorkspaceCommandHandler
    : IRequestHandler<CreateHouseholdWorkspaceCommand, WorkspaceSummaryDto>
{
    private readonly IAppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateHouseholdWorkspaceCommandHandler(
        IAppDbContext dbContext,
        ICurrentUserService currentUserService,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<WorkspaceSummaryDto> Handle(
        CreateHouseholdWorkspaceCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetRequiredUserId();
        var now = _dateTimeProvider.UtcNow;

        var workspace = Workspace.CreateHousehold(
            userId,
            request.Name,
            request.DefaultCurrency,
            now);

        var defaultCategories = DefaultCategoryCatalog.CreateForWorkspace(workspace.Id);

        var auditLog = AuditLog.Create(
            workspace.Id,
            userId,
            "HouseholdWorkspaceCreated",
            nameof(Workspace),
            workspace.Id,
            oldValuesJson: null,
            newValuesJson: AuditJson.Serialize(new { workspace.Name, workspace.DefaultCurrency }),
            utcNow: now);

        _dbContext.Workspaces.Add(workspace);
        _dbContext.Categories.AddRange(defaultCategories);
        _dbContext.AuditLogs.Add(auditLog);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new WorkspaceSummaryDto(
            workspace.Id,
            workspace.Name,
            workspace.Type,
            workspace.DefaultCurrency,
            WorkspaceRole.Owner,
            workspace.CreatedAtUtc,
            now);
    }
}
