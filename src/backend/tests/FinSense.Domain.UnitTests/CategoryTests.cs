using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FluentAssertions;

namespace FinSense.Domain.UnitTests;

public class CategoryTests
{
    private readonly Guid _workspaceId = Guid.NewGuid();

    [Fact]
    public void SystemCategory_Update_ShouldThrowDomainException()
    {
        var systemCategory = Category.CreateSystem(
            _workspaceId,
            "Market",
            "Groceries",
            CategoryType.Expense,
            "shopping-cart",
            "#10B981");

        var act = () => systemCategory.Update("Süpermarket", "Supermarket", "cart", "#000000");

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == "SYSTEM_CATEGORY_IMMUTABLE");
    }

    [Fact]
    public void SystemCategory_SoftDelete_ShouldThrowDomainException()
    {
        var systemCategory = Category.CreateSystem(
            _workspaceId,
            "Faturalar",
            "Bills",
            CategoryType.Expense,
            "file-text",
            "#EF4444");

        var act = () => systemCategory.SoftDelete();

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == "SYSTEM_CATEGORY_IMMUTABLE");
    }

    [Fact]
    public void CustomCategory_UpdateAndSoftDelete_ShouldSucceed()
    {
        var customCategory = Category.CreateCustom(
            _workspaceId,
            "Evcil Hayvan",
            "Pets",
            CategoryType.Expense,
            "paw",
            "#F59E0B");

        customCategory.IsSystem.Should().BeFalse();

        customCategory.Update("Evcil Hayvan Bakımı", "Pet Care", "heart", "#8B5CF6");
        customCategory.NameTr.Should().Be("Evcil Hayvan Bakımı");
        customCategory.NameEn.Should().Be("Pet Care");

        customCategory.SoftDelete();
        customCategory.IsDeleted.Should().BeTrue();
    }
}
