using PaymentIntegration.Domain.Enums;

namespace PaymentIntegration.Domain.Entities;

/// <summary>
/// One payment attempt. The idempotency key and invoice number are both unique,
/// so the database itself rejects a second row for the same checkout click.
/// </summary>
public sealed class Transaction
{
    public long Id { get; set; }

    public int CustomerId { get; set; }

    public int ProductId { get; set; }

    public string InvoiceNo { get; set; } = "";

    public string IdempotencyKey { get; set; } = "";

    /// <summary>
    /// customerId:productId:cardId. If the same key comes back with a different
    /// purchase, we refuse it instead of replaying the old payment.
    /// </summary>
    public string RequestFingerprint { get; set; } = "";

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "SGD";

    public TransactionStatus Status { get; set; } = TransactionStatus.Pending;

    public string? PaymentChannel { get; set; }

    public string? MaskedCardNo { get; set; }

    public string? ProviderRef { get; set; }

    public string? RespCode { get; set; }

    public string? RespDesc { get; set; }

    /// <summary>2C2P payment token. Kept for support. Never sent to the browser.</summary>
    public string? PaymentToken { get; set; }

    /// <summary>
    /// The first POST /api/checkout body we returned. A retry with the same key
    /// sends this again and does not call 2C2P.
    /// </summary>
    public string? CheckoutResponseJson { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? PaidAt { get; set; }

    public Customer? Customer { get; set; }

    public Product? Product { get; set; }
}
