using FinSense.Domain.Entities;
using FinSense.Domain.Enums;
using FinSense.Domain.Exceptions;
using FluentAssertions;

namespace FinSense.Domain.UnitTests;

public class TransactionTests
{
    private readonly Guid _workspaceId = Guid.NewGuid();
    private readonly Guid _walletId = Guid.NewGuid();
    private readonly Guid _counterpartyWalletId = Guid.NewGuid();
    private readonly Guid _categoryId = Guid.NewGuid();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-99.99)]
    public void Create_WithZeroOrNegativeAmount_ShouldThrowDomainException(decimal invalidAmount)
    {
        var act = () => Transaction.Create(
            _workspaceId,
            _walletId,
            TransactionType.Expense,
            invalidAmount,
            new DateOnly(2026, 11, 10),
            categoryId: _categoryId);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_TRANSACTION_AMOUNT");
    }

    [Fact]
    public void Create_ExpenseWithoutCategory_ShouldThrowDomainException()
    {
        var act = () => Transaction.Create(
            _workspaceId,
            _walletId,
            TransactionType.Expense,
            250m,
            new DateOnly(2026, 11, 10),
            categoryId: null);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == "TRANSACTION_CATEGORY_REQUIRED");
    }

    [Fact]
    public void Create_ExpenseWithCounterpartyWallet_ShouldThrowDomainException()
    {
        var act = () => Transaction.Create(
            _workspaceId,
            _walletId,
            TransactionType.Expense,
            250m,
            new DateOnly(2026, 11, 10),
            categoryId: _categoryId,
            counterpartyWalletId: _counterpartyWalletId);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == "COUNTERPARTY_WALLET_NOT_ALLOWED");
    }

    [Fact]
    public void Create_TransferWithoutCounterpartyWallet_ShouldThrowDomainException()
    {
        var act = () => Transaction.Create(
            _workspaceId,
            _walletId,
            TransactionType.Transfer,
            1000m,
            new DateOnly(2026, 11, 10),
            counterpartyWalletId: null);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == "TRANSFER_COUNTERPARTY_REQUIRED");
    }

    [Fact]
    public void Create_TransferWithSameSourceAndDestinationWallet_ShouldThrowDomainException()
    {
        var act = () => Transaction.Create(
            _workspaceId,
            _walletId,
            TransactionType.Transfer,
            1000m,
            new DateOnly(2026, 11, 10),
            counterpartyWalletId: _walletId);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == "TRANSFER_SAME_WALLET_FORBIDDEN");
    }

    [Fact]
    public void Create_TransferWithCategory_ShouldThrowDomainException()
    {
        var act = () => Transaction.Create(
            _workspaceId,
            _walletId,
            TransactionType.Transfer,
            1000m,
            new DateOnly(2026, 11, 10),
            categoryId: _categoryId,
            counterpartyWalletId: _counterpartyWalletId);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == "TRANSFER_CATEGORY_MUST_BE_NULL");
    }

    [Fact]
    public void ApplyToWallets_And_SoftDelete_ForCreditCardSettlementTransfer_ShouldReconcileBalances()
    {
        var bankAccount = Wallet.Create(_workspaceId, "Banka Hesabı", WalletType.BankAccount, initialBalance: 10000m);
        var creditCard = Wallet.Create(_workspaceId, "Kredi Kartı", WalletType.CreditCard, initialBalance: -1000m);

        var transfer = Transaction.Create(
            _workspaceId,
            bankAccount.Id,
            TransactionType.Transfer,
            1000m,
            new DateOnly(2026, 11, 10),
            counterpartyWalletId: creditCard.Id);

        transfer.ApplyToWallets(bankAccount, creditCard);

        bankAccount.CurrentBalance.Should().Be(9000m);
        creditCard.CurrentBalance.Should().Be(0m);

        transfer.SoftDelete(DateTime.UtcNow, bankAccount, creditCard);

        transfer.IsDeleted.Should().BeTrue();
        transfer.DeletedAtUtc.Should().NotBeNull();
        bankAccount.CurrentBalance.Should().Be(10000m);
        creditCard.CurrentBalance.Should().Be(-1000m);
    }

    [Fact]
    public void ApplyToWallets_WhenCounterpartyBelongsToDifferentWorkspace_ShouldThrowDomainException()
    {
        var sourceWallet = Wallet.Create(_workspaceId, "Kaynak", WalletType.BankAccount, initialBalance: 5000m);
        var foreignWallet = Wallet.Create(Guid.NewGuid(), "Yabancı Cüzdan", WalletType.BankAccount, initialBalance: 0m);

        var transfer = Transaction.Create(
            _workspaceId,
            sourceWallet.Id,
            TransactionType.Transfer,
            500m,
            new DateOnly(2026, 11, 10),
            counterpartyWalletId: foreignWallet.Id);

        var act = () => transfer.ApplyToWallets(sourceWallet, foreignWallet);

        act.Should().Throw<DomainException>()
            .Where(e => e.ErrorCode == "INVALID_TRANSFER_COUNTERPARTY");
    }
}
