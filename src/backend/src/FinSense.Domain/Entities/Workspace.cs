using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;

namespace FinSense.Domain.Entities;

public class Workspace
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public WorkspaceType Type { get; private set; }
    public string DefaultCurrency { get; private set; } = "TRY";
    public Guid? CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public User? CreatedByUser { get; private set; }

    private readonly List<WorkspaceMember> _members = [];
    public IReadOnlyCollection<WorkspaceMember> Members => _members.AsReadOnly();

    private readonly List<WorkspaceInvitation> _invitations = [];
    public IReadOnlyCollection<WorkspaceInvitation> Invitations => _invitations.AsReadOnly();

    private readonly List<Wallet> _wallets = [];
    public IReadOnlyCollection<Wallet> Wallets => _wallets.AsReadOnly();

    private readonly List<Category> _categories = [];
    public IReadOnlyCollection<Category> Categories => _categories.AsReadOnly();

    private readonly List<Transaction> _transactions = [];
    public IReadOnlyCollection<Transaction> Transactions => _transactions.AsReadOnly();

    private readonly List<Budget> _budgets = [];
    public IReadOnlyCollection<Budget> Budgets => _budgets.AsReadOnly();

    private readonly List<SavingsGoal> _savingsGoals = [];
    public IReadOnlyCollection<SavingsGoal> SavingsGoals => _savingsGoals.AsReadOnly();

    private readonly List<Receipt> _receipts = [];
    public IReadOnlyCollection<Receipt> Receipts => _receipts.AsReadOnly();

    private readonly List<AiConversation> _aiConversations = [];
    public IReadOnlyCollection<AiConversation> AiConversations => _aiConversations.AsReadOnly();

    private readonly List<AuditLog> _auditLogs = [];
    public IReadOnlyCollection<AuditLog> AuditLogs => _auditLogs.AsReadOnly();

    private Workspace() { }

    public static Workspace CreatePersonal(
        Guid ownerUserId,
        string name = "Kişisel Alanım",
        string defaultCurrency = "TRY",
        DateTime? utcNow = null)
    {
        return Create(ownerUserId, name, WorkspaceType.Personal, defaultCurrency, utcNow);
    }

    public static Workspace CreateHousehold(
        Guid ownerUserId,
        string name,
        string defaultCurrency = "TRY",
        DateTime? utcNow = null)
    {
        return Create(ownerUserId, name, WorkspaceType.Household, defaultCurrency, utcNow);
    }

    private static Workspace Create(
        Guid ownerUserId,
        string name,
        WorkspaceType type,
        string defaultCurrency,
        DateTime? utcNow)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new DomainException("INVALID_OWNER_USER_ID", "Çalışma alanı kurucusu geçerli bir kullanıcı olmalıdır.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("WORKSPACE_NAME_REQUIRED", "Çalışma alanı adı zorunludur.");
        }

        var normalizedCurrency = NormalizeCurrencyCode(defaultCurrency);
        var now = utcNow ?? DateTime.UtcNow;

        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Type = type,
            DefaultCurrency = normalizedCurrency,
            CreatedByUserId = ownerUserId,
            CreatedAtUtc = now
        };

        workspace._members.Add(WorkspaceMember.Create(workspace.Id, ownerUserId, WorkspaceRole.Owner, now));
        return workspace;
    }

    public void EnsureCanBeDeletedIndependently()
    {
        if (Type == WorkspaceType.Personal)
        {
            throw new DomainException(
                "PERSONAL_WORKSPACE_CANNOT_BE_DELETED",
                "Kişisel çalışma alanı hesap silinmeden bağımsız olarak silinemez.");
        }
    }

    internal static string NormalizeCurrencyCode(string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Trim().Length != 3)
        {
            throw new DomainException("INVALID_CURRENCY_CODE", "Para birimi kodu 3 karakterli ISO 4217 formatında olmalıdır (örn. TRY).");
        }

        return currencyCode.Trim().ToUpperInvariant();
    }
}
