namespace PaymentIntegration.Domain.Enums;

/// <summary>
/// Pending means we asked 2C2P but do not have a final answer yet.
/// Only the backend notification, or a Payment Inquiry, may leave Pending.
/// </summary>
public enum TransactionStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3
}
