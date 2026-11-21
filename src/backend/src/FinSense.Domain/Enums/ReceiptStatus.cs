namespace FinSense.Domain.Enums;

public enum ReceiptStatus : short
{
    PendingConfirmation = 1,
    LinkedToTransaction = 2,
    MarkedForDeletion = 3
}
