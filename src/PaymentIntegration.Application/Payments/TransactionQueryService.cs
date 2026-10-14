using PaymentIntegration.Application.Abstractions;
using PaymentIntegration.Application.Common;
using PaymentIntegration.Application.Contracts;

namespace PaymentIntegration.Application.Payments;

/// <summary>
/// Reads a transaction straight from MySQL. Do not put a cache in front of this.
/// </summary>
public sealed class TransactionQueryService
{
    private readonly ITransactionRepository _transactions;

    public TransactionQueryService(ITransactionRepository transactions)
    {
        _transactions = transactions;
    }

    public async Task<TransactionResponse> GetAsync(string invoiceNo, CancellationToken cancellationToken)
    {
        var transaction = await _transactions.GetResponseAsync(invoiceNo, cancellationToken);
        if (transaction is null)
        {
            throw AppException.NotFound($"No payment was found for invoice {invoiceNo}.");
        }

        return transaction;
    }
}
