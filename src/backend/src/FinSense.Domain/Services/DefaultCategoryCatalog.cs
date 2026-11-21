using FinSense.Domain.Entities;
using FinSense.Domain.Enums;

namespace FinSense.Domain.Services;

public static class DefaultCategoryCatalog
{
    public static IReadOnlyList<Category> CreateForWorkspace(Guid workspaceId)
    {
        return
        [
            // Expense Categories (Gider Kategorileri)
            Category.CreateSystem(workspaceId, "Market", "Groceries", CategoryType.Expense, "shopping-cart", "#10B981"),
            Category.CreateSystem(workspaceId, "Kira", "Rent", CategoryType.Expense, "home", "#3B82F6"),
            Category.CreateSystem(workspaceId, "Faturalar", "Bills", CategoryType.Expense, "file-text", "#EF4444"),
            Category.CreateSystem(workspaceId, "Ulaşım", "Transport", CategoryType.Expense, "car", "#F59E0B"),
            Category.CreateSystem(workspaceId, "Yeme-İçme", "Dining", CategoryType.Expense, "utensils", "#EC4899"),
            Category.CreateSystem(workspaceId, "Sağlık", "Health", CategoryType.Expense, "heart-pulse", "#14B8A6"),
            Category.CreateSystem(workspaceId, "Alışveriş", "Shopping", CategoryType.Expense, "shopping-bag", "#8B5CF6"),
            Category.CreateSystem(workspaceId, "Abonelikler", "Subscriptions", CategoryType.Expense, "repeat", "#6366F1"),

            // Income Categories (Gelir Kategorileri)
            Category.CreateSystem(workspaceId, "Maaş", "Salary", CategoryType.Income, "wallet", "#22C55E"),
            Category.CreateSystem(workspaceId, "Freelance", "Freelance", CategoryType.Income, "laptop", "#06B6D4"),
            Category.CreateSystem(workspaceId, "Ek Gelir", "Side Income", CategoryType.Income, "trending-up", "#84CC16"),
            Category.CreateSystem(workspaceId, "Diğer Gelirler", "Other Income", CategoryType.Income, "plus-circle", "#10B981")
        ];
    }
}
