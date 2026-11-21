using FinSense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinSense.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Workspace> Workspaces { get; }
    DbSet<WorkspaceMember> WorkspaceMembers { get; }
    DbSet<WorkspaceInvitation> WorkspaceInvitations { get; }
    DbSet<Wallet> Wallets { get; }
    DbSet<Category> Categories { get; }
    DbSet<Transaction> Transactions { get; }
    DbSet<RecurringTransaction> RecurringTransactions { get; }
    DbSet<InvestmentTransaction> InvestmentTransactions { get; }
    DbSet<Budget> Budgets { get; }
    DbSet<SavingsGoal> SavingsGoals { get; }
    DbSet<Receipt> Receipts { get; }
    DbSet<AiConversation> AiConversations { get; }
    DbSet<AiMessage> AiMessages { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
