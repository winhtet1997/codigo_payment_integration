using Microsoft.Extensions.Logging;
using PaymentIntegration.Application.Abstractions;
using PaymentIntegration.Application.Catalog;
using PaymentIntegration.Application.Common;
using PaymentIntegration.Application.Contracts;
using PaymentIntegration.Domain.Enums;

namespace PaymentIntegration.Application.Payments;

/// <summary>
/// Turns a verified 2C2P result into a final transaction row.
/// The notification and Payment Inquiry both come through here, so the rules
/// live in one place: check the signature first (the caller did that), check
/// the money, then update only while status is still Pending.
/// </summary>
public sealed class PaymentUpdateService
{
    private readonly ITransactionRepository _transactions;
    private readonly ICustomerCardRepository _cards;
    private readonly IPaymentGateway _gateway;
    private readonly ISignedPayloadReader _reader;
    private readonly IAppCache _cache;
    private readonly ILogger<PaymentUpdateService> _logger;

    public PaymentUpdateService(
        ITransactionRepository transactions,
        ICustomerCardRepository cards,
        IPaymentGateway gateway,
        ISignedPayloadReader reader,
        IAppCache cache,
        ILogger<PaymentUpdateService> logger)
    {
        _transactions = transactions;
        _cards = cards;
        _gateway = gateway;
        _reader = reader;
        _cache = cache;
        _logger = logger;
    }

    public Task ApplyVerifiedResultAsync(PaymentResultNotice notice, bool isNotification, CancellationToken cancellationToken) =>
        ApplyAsync(notice, isNotification, cancellationToken);

    public async Task HandleNotificationAsync(string payload, CancellationToken cancellationToken)
    {
        PaymentResultNotice notice;
        try
        {
            notice = _reader.ReadNotification(payload);
        }
        catch (InvalidPaymentSignatureException)
        {
            throw AppException.BadRequest("The notification signature could not be verified.");
        }

        if (string.IsNullOrWhiteSpace(notice.InvoiceNo))
        {
            throw AppException.BadRequest("The notification did not include an invoice number.");
        }

        // A notification is the final word. Anything other than 0000 is a failure.
        await ApplyAsync(notice, isNotification: true, cancellationToken);
    }

    /// <summary>
    /// Asks 2C2P what happened, then applies that result the same way as a notification.
    /// Saved-card payments always do this. Hosted payments use it when the browser
    /// returns before (or without) the backend notification.
    /// </summary>
    public async Task<TransactionResponse> InquireAsync(string invoiceNo, CancellationToken cancellationToken)
    {
        var current = await _transactions.GetResponseAsync(invoiceNo, cancellationToken)
            ?? throw AppException.NotFound($"No payment was found for invoice {invoiceNo}.");

        if (current.Status != TransactionStatus.Pending.ToString())
        {
            return current;
        }

        var notice = await _gateway.InquireAsync(invoiceNo, cancellationToken);
        await ApplyAsync(notice, isNotification: false, cancellationToken);

        return await _transactions.GetResponseAsync(invoiceNo, cancellationToken) ?? current;
    }

    private async Task ApplyAsync(PaymentResultNotice notice, bool isNotification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notice.InvoiceNo))
        {
            throw AppException.BadRequest("The payment result did not include an invoice number.");
        }

        var transaction = await _transactions.FindByInvoiceNoAsync(notice.InvoiceNo, cancellationToken);
        if (transaction is null)
        {
            _logger.LogWarning("Ignoring a payment result for unknown invoice {InvoiceNo}.", notice.InvoiceNo);
            if (isNotification)
            {
                // 2C2P retries any non-200. An invoice we never created will never match,
                // so we acknowledge it and keep the warning in the logs.
                return;
            }

            throw AppException.NotFound($"No payment was found for invoice {notice.InvoiceNo}.");
        }

        if (notice.Amount is null || string.IsNullOrWhiteSpace(notice.CurrencyCode))
        {
            throw AppException.BadRequest("The payment result did not include an amount and currency.");
        }

        var amountMatches = decimal.Round(transaction.Amount, 2) == decimal.Round(notice.Amount.Value, 2);
        var currencyMatches = string.Equals(transaction.Currency, notice.CurrencyCode, StringComparison.OrdinalIgnoreCase);
        // The JT01 sandbox settles in THB. amount/currencyCode are then the converted
        // value, and fxAmount/fxCurrencyCode are the SGD amount we sent.
        if ((!amountMatches || !currencyMatches)
            && notice.FxAmount is not null
            && !string.IsNullOrWhiteSpace(notice.FxCurrencyCode))
        {
            amountMatches = decimal.Round(transaction.Amount, 2) == decimal.Round(notice.FxAmount.Value, 2);
            currencyMatches = string.Equals(transaction.Currency, notice.FxCurrencyCode, StringComparison.OrdinalIgnoreCase);
        }

        if (!amountMatches || !currencyMatches)
        {
            _logger.LogWarning(
                "Amount mismatch for {InvoiceNo}. Stored {StoredAmount} {StoredCurrency}, provider sent {ProviderAmount} {ProviderCurrency}.",
                transaction.InvoiceNo,
                transaction.Amount,
                transaction.Currency,
                notice.Amount,
                notice.CurrencyCode);
            throw AppException.BadRequest("The payment result amount or currency does not match the stored transaction.");
        }

        var nextStatus = DecideStatus(notice.RespCode, isNotification);
        if (nextStatus is not null)
        {
            var updated = await _transactions.TryFinalizeAsync(
                transaction.InvoiceNo,
                nextStatus,
                TextLimits.Clip(notice.ChannelCode, 20),
                TextLimits.Clip(notice.AccountNo, 30),
                TextLimits.Clip(notice.TranRef, 50),
                TextLimits.Clip(notice.RespCode, 10),
                TextLimits.Clip(notice.RespDesc, 255),
                nextStatus == TransactionStatus.Succeeded.ToString() ? DateTime.UtcNow : null,
                cancellationToken);

            _logger.LogInformation(
                "Payment {InvoiceNo} result {RespCode}. Status change applied: {Applied}.",
                transaction.InvoiceNo,
                notice.RespCode,
                updated);
        }

        if (!string.IsNullOrWhiteSpace(notice.CustomerToken))
        {
            var masked = string.IsNullOrWhiteSpace(notice.AccountNo) ? "Saved card" : notice.AccountNo.Trim();
            var saved = await _cards.SaveIfNewAsync(transaction.CustomerId, notice.CustomerToken.Trim(), masked, cancellationToken);
            if (saved)
            {
                try
                {
                    await _cache.RemoveAsync(CatalogService.CardsKey(transaction.CustomerId), cancellationToken);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Could not clear the saved-card cache for customer {CustomerId}.", transaction.CustomerId);
                }

                _logger.LogInformation("Saved a new card token for customer {CustomerId}.", transaction.CustomerId);
            }
        }
    }

    private static string? DecideStatus(string? respCode, bool isNotification)
    {
        if (ProviderResponseCodes.IsSuccess(respCode))
        {
            return TransactionStatus.Succeeded.ToString();
        }

        if (!isNotification && ProviderResponseCodes.IsStillPending(respCode))
        {
            return null;
        }

        return TransactionStatus.Failed.ToString();
    }
}
