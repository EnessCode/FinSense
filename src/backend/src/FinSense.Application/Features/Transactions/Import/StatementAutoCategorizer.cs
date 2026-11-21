using FinSense.Domain.Entities;
using FinSense.Domain.Enums;

namespace FinSense.Application.Features.Transactions.Import;

/// <summary>
/// Deterministic, 100% local keyword-based categorizer that maps Turkish and international
/// bank statement descriptions to existing workspace categories.
/// </summary>
public static class StatementAutoCategorizer
{
    private sealed record CategoryRule(string TargetNameTr, string TargetNameEn, CategoryType Type, string[] Keywords);

    private static readonly CategoryRule[] Rules =
    [
        // Expense rules
        new(
            "Market",
            "Groceries",
            CategoryType.Expense,
            [
                "MIGROS", "BIM", "A101", "SOK", "CARREFOUR", "MACRO", "FILE MARKET",
                "HAKMAR", "METRO GROSS", "MARKET", "BAKKAL", "MANAV", "SARKUTERI"
            ]),
        new(
            "Yeme-İçme",
            "Dining",
            CategoryType.Expense,
            [
                "STARBUCKS", "YEMEKSEPETI", "GETIR", "KAHVE", "ESPRESSOLAB", "MCDONALD",
                "BURGER KING", "DOMINOS", "RESTORAN", "CAFE", "KAFE", "LOKANTA",
                "TRENDYOL YEMEK", "POPPEYES", "KFC", "SIMITSARAYI", "PASTA"
            ]),
        new(
            "Abonelikler",
            "Subscriptions",
            CategoryType.Expense,
            [
                "SPOTIFY", "NETFLIX", "YOUTUBE", "APPLE", "GOOGLE", "AMAZON PRIME",
                "DISNEY", "BLUTV", "EXXEN", "ICLOUD", "CHATGPT", "OPENAI", "ADOBE",
                "MICROSOFT", "PLAYSTATION", "STEAM", "XBOX"
            ]),
        new(
            "Ulaşım",
            "Transport",
            CategoryType.Expense,
            [
                "SHELL", "OPET", "BP ", "PETROL OFISI", "TOTALENERGIES", "ISTANBULKART",
                "UBER", "BITAKSI", "TAKSI", "MARTI", "THY", "TURKISH AIRLINES", "PEGASUS",
                "TCDD", "HGS", "OGS", "OTOPARK", "AKARYAKIT", "BENZIN"
            ]),
        new(
            "Faturalar",
            "Bills",
            CategoryType.Expense,
            [
                "TURKCELL", "VODAFONE", "TURK TELEKOM", "SUPERONLINE", "TURKNET",
                "IGDAS", "ISKI", "ELEKTRIK", "ENERJISA", "CK BOGAZICI", "DOGALGAZ",
                "SU FATURA", "FATURA", "TELEKOM"
            ]),
        new(
            "Kira",
            "Rent",
            CategoryType.Expense,
            ["KIRA", "AIDAT", "APARTMAN", "SITE YONETIM"]),
        new(
            "Sağlık",
            "Health",
            CategoryType.Expense,
            ["ECZANE", "HASTANE", "KLINIK", "DOKTOR", "ACIBADEM", "MEMORIAL", "MEDIPOL", "SAGLIK"]),
        new(
            "Alışveriş",
            "Shopping",
            CategoryType.Expense,
            [
                "TRENDYOL", "HEPSIBURADA", "AMAZON", "N11", "ZARA", "LCW", "LC WAIKIKI",
                "MAVI", "BOYNER", "TEKNOSA", "MEDIAMARKT", "VATAN", "IKEA", "DECATHLON"
            ]),

        // Income rules
        new(
            "Maaş",
            "Salary",
            CategoryType.Income,
            ["MAAS", "SALARY", "UCRET", "BORDRO", "PRIM"]),
        new(
            "Freelance",
            "Freelance",
            CategoryType.Income,
            ["FREELANCE", "DANISMANLIK", "UPWORK", "FIVERR"]),
        new(
            "Ek Gelir",
            "Side Income",
            CategoryType.Income,
            ["TEMETTU", "FAIZ", "KIRA GELIRI", "IADE", "CASHBACK", "BONUS"])
    ];

    public static Category? Categorize(
        string description,
        TransactionType transactionType,
        IReadOnlyList<Category> workspaceCategories)
    {
        var categoryType = transactionType == TransactionType.Income
            ? CategoryType.Income
            : CategoryType.Expense;

        var candidates = workspaceCategories
            .Where(c => !c.IsDeleted && c.Type == categoryType)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var normalized = NormalizeForMatch(description);

        foreach (var rule in Rules)
        {
            if (rule.Type != categoryType)
            {
                continue;
            }

            if (rule.Keywords.Any(kw => MatchesKeyword(normalized, kw)))
            {
                var matched = FindCategory(candidates, rule.TargetNameTr, rule.TargetNameEn);
                if (matched is not null)
                {
                    return matched;
                }
            }
        }

        // Direct match against custom workspace category names
        foreach (var cat in candidates)
        {
            var catNormTr = NormalizeForMatch(cat.NameTr);
            if (catNormTr.Length >= 3 && normalized.Contains(catNormTr, StringComparison.Ordinal))
            {
                return cat;
            }
        }

        // Sensible default fallback so every preview row has a valid category
        if (categoryType == CategoryType.Income)
        {
            return FindCategory(candidates, "Diğer Gelirler", "Other Income") ?? candidates[0];
        }

        return FindCategory(candidates, "Alışveriş", "Shopping") ?? candidates[0];
    }

    private static bool MatchesKeyword(string normalizedDescription, string keyword)
    {
        if (keyword.EndsWith(' '))
        {
            var trimmed = keyword.TrimEnd();
            return normalizedDescription == trimmed ||
                   normalizedDescription.StartsWith(keyword, StringComparison.Ordinal) ||
                   normalizedDescription.Contains($" {trimmed} ", StringComparison.Ordinal) ||
                   normalizedDescription.EndsWith($" {trimmed}", StringComparison.Ordinal);
        }

        return normalizedDescription.Contains(keyword, StringComparison.Ordinal);
    }

    private static Category? FindCategory(List<Category> candidates, string nameTr, string nameEn)
    {
        var normTr = NormalizeForMatch(nameTr);
        var normEn = NormalizeForMatch(nameEn);

        return candidates.FirstOrDefault(c =>
            NormalizeForMatch(c.NameTr) == normTr ||
            (c.NameEn is not null && NormalizeForMatch(c.NameEn) == normEn));
    }

    public static string NormalizeForMatch(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        Span<char> buffer = input.Length <= 256 ? stackalloc char[input.Length] : new char[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            buffer[i] = input[i] switch
            {
                'ı' or 'İ' or 'I' or 'i' => 'I',
                'ğ' or 'Ğ' => 'G',
                'ş' or 'Ş' => 'S',
                'ö' or 'Ö' => 'O',
                'ü' or 'Ü' => 'U',
                'ç' or 'Ç' => 'C',
                _ => char.ToUpperInvariant(input[i])
            };
        }

        return new string(buffer).Trim();
    }
}
