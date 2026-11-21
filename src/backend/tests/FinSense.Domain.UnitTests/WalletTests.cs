using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FluentAssertions;

namespace FinSense.Domain.UnitTests;

public class WalletTests
{
    private readonly Guid _workspaceId = Guid.NewGuid();

    [Fact]
    public void Create_ShouldInitializeBalancesAndMetadataCorrectly()
    {
        var wallet = Wallet.Create(
            _workspaceId,
            "Maaş Hesabı",
            WalletType.BankAccount,
            initialBalance: 25000.55556m,
            currencyCode: "try");

        wallet.WorkspaceId.Should().Be(_workspaceId);
        wallet.Name.Should().Be("Maaş Hesabı");
        wallet.Type.Should().Be(WalletType.BankAccount);
        wallet.CurrencyCode.Should().Be("TRY");
        wallet.InitialBalance.Should().Be(25000.5556m);
        wallet.CurrentBalance.Should().Be(25000.5556m);
        wallet.IsArchived.Should().BeFalse();
        wallet.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void ApplyIncome_And_ApplyExpense_ShouldMutateCurrentBalanceAccurately()
    {
        var wallet = Wallet.Create(_workspaceId, "Nakit", WalletType.Cash, initialBalance: 1000m);

        wallet.ApplyIncome(500.25m);
        wallet.CurrentBalance.Should().Be(1500.25m);

        wallet.ApplyExpense(300.10m);
        wallet.CurrentBalance.Should().Be(1200.15m);
        wallet.InitialBalance.Should().Be(1000m);
    }

    [Fact]
    public void ApplyExpense_OnCreditCardWallet_ShouldDecreaseBalanceIntoNegativeDebt()
    {
        var creditCard = Wallet.Create(
            _workspaceId,
            "Bonus Kredi Kartı",
            WalletType.CreditCard,
            initialBalance: 0m,
            creditLimit: 50000m);

        creditCard.ApplyExpense(1420.75m);

        creditCard.CurrentBalance.Should().Be(-1420.75m);
    }

    [Fact]
    public void AdjustInitialBalance_ShouldShiftCurrentBalanceByDelta()
    {
        var wallet = Wallet.Create(_workspaceId, "Vadesiz Hesap", WalletType.BankAccount, initialBalance: 1000m);
        wallet.ApplyExpense(200m); // CurrentBalance = 800

        var delta = wallet.AdjustInitialBalance(1500m); // +500 delta

        delta.Should().Be(500m);
        wallet.InitialBalance.Should().Be(1500m);
        wallet.CurrentBalance.Should().Be(1300m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-500)]
    public void ApplyIncomeOrExpense_WithNonPositiveAmount_ShouldThrowDomainException(decimal invalidAmount)
    {
        var wallet = Wallet.Create(_workspaceId, "Nakit", WalletType.Cash, initialBalance: 100m);

        var actIncome = () => wallet.ApplyIncome(invalidAmount);
        var actExpense = () => wallet.ApplyExpense(invalidAmount);

        actIncome.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_TRANSACTION_AMOUNT");
        actExpense.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_TRANSACTION_AMOUNT");
    }

    [Fact]
    public void ApplyIncome_WhenWalletIsArchived_ShouldThrowDomainException()
    {
        var wallet = Wallet.Create(_workspaceId, "Eski Hesap", WalletType.BankAccount);
        wallet.Archive();

        var act = () => wallet.ApplyIncome(100m);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == "WALLET_ARCHIVED");
    }
}
