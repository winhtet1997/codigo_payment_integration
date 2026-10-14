using PaymentIntegration.Application.Contracts;
using PaymentIntegration.Domain.Entities;

namespace PaymentIntegration.Application.Abstractions;

public interface ICustomerRepository
{
    Task<IReadOnlyList<CustomerResponse>> ListAsync(CancellationToken cancellationToken);

    Task<Customer?> FindAsync(int id, CancellationToken cancellationToken);
}

public interface IProductRepository
{
    Task<IReadOnlyList<ProductResponse>> ListAsync(CancellationToken cancellationToken);

    Task<Product?> FindAsync(int id, CancellationToken cancellationToken);
}

public interface ICustomerCardRepository
{
    Task<IReadOnlyList<SavedCardResponse>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken);

    Task<CustomerCard?> FindAsync(int id, CancellationToken cancellationToken);

    /// <summary>Returns false when this customer already has this token.</summary>
    Task<bool> SaveIfNewAsync(int customerId, string customerToken, string maskedCardNo, CancellationToken cancellationToken);
}

public interface ITransactionRepository
{
    Task<Transaction?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task<Transaction?> FindByInvoiceNoAsync(string invoiceNo, CancellationToken cancellationToken);

    Task<TransactionResponse?> GetResponseAsync(string invoiceNo, CancellationToken cancellationToken);

    /// <summary>Returns false when another request already inserted this idempotency key.</summary>
    Task<bool> TryAddAsync(Transaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the checkout reply and payment token. Does not touch status or the
    /// response code once the row is no longer Pending.
    /// </summary>
    Task SaveCheckoutSnapshotAsync(
        long transactionId,
        string? paymentToken,
        string checkoutResponseJson,
        string? respCode,
        string? respDesc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates a row only while it is still Pending. Returns false when the
    /// payment was already final, which is how a repeated notification stays safe.
    /// </summary>
    Task<bool> TryFinalizeAsync(
        string invoiceNo,
        string status,
        string? paymentChannel,
        string? maskedCardNo,
        string? providerRef,
        string? respCode,
        string? respDesc,
        DateTime? paidAt,
        CancellationToken cancellationToken);
}
