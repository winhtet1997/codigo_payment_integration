using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentIntegration.Application.Abstractions;
using PaymentIntegration.Application.Common;
using PaymentIntegration.Application.Contracts;
using PaymentIntegration.Application.Payments;
using PaymentIntegration.Domain.Entities;
using PaymentIntegration.Domain.Enums;

namespace PaymentIntegration.Application.Checkout;

public sealed class AppOptions
{
    public const string SectionName = "App";

    public string FrontendBaseUrl { get; set; } = "http://localhost:3000";

    /// <summary>
    /// Public URL 2C2P can reach. On a laptop this is an ngrok or Cloudflare Tunnel URL.
    /// </summary>
    public string PublicApiBaseUrl { get; set; } = "http://localhost:8080";
}

/// <summary>
/// One checkout click. The Redis lock and the unique idempotency_key together
/// make a double-click create one transaction and one 2C2P call.
/// </summary>
public sealed class CheckoutService
{
    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDistributedLock _lock;
    private readonly ICustomerRepository _customers;
    private readonly IProductRepository _products;
    private readonly ICustomerCardRepository _cards;
    private readonly ITransactionRepository _transactions;
    private readonly IPaymentGateway _gateway;
    private readonly PaymentUpdateService _updates;
    private readonly AppOptions _app;
    private readonly ILogger<CheckoutService> _logger;

    public CheckoutService(
        IDistributedLock distributedLock,
        ICustomerRepository customers,
        IProductRepository products,
        ICustomerCardRepository cards,
        ITransactionRepository transactions,
        IPaymentGateway gateway,
        PaymentUpdateService updates,
        IOptions<AppOptions> appOptions,
        ILogger<CheckoutService> logger)
    {
        _lock = distributedLock;
        _customers = customers;
        _products = products;
        _cards = cards;
        _transactions = transactions;
        _gateway = gateway;
        _updates = updates;
        _app = appOptions.Value;
        _logger = logger;
    }

    public Task<CheckoutResponse> CheckoutAsync(CheckoutCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Length > 100)
        {
            throw AppException.BadRequest("The Idempotency-Key header is required and must be at most 100 characters.");
        }

        var key = command.IdempotencyKey.Trim();
        return _lock.ExecuteAsync(
            $"checkout:{key}",
            () => CheckoutUnderLockAsync(command, key, cancellationToken),
            cancellationToken);
    }

    private async Task<CheckoutResponse> CheckoutUnderLockAsync(
        CheckoutCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var fingerprint = CheckoutFingerprint.Create(command.CustomerId, command.ProductId, command.CustomerCardId);
        var existing = await _transactions.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, fingerprint);
        }

        var customer = await _customers.FindAsync(command.CustomerId, cancellationToken)
            ?? throw AppException.NotFound($"Customer {command.CustomerId} was not found.");

        var product = await _products.FindAsync(command.ProductId, cancellationToken)
            ?? throw AppException.NotFound($"Product {command.ProductId} was not found.");

        if (!string.Equals(product.Currency, "SGD", StringComparison.OrdinalIgnoreCase))
        {
            throw AppException.BadRequest("This checkout only charges SGD.");
        }

        CustomerCard? card = null;
        if (command.CustomerCardId is int cardId)
        {
            card = await _cards.FindAsync(cardId, cancellationToken);
            if (card is null || card.CustomerId != customer.Id)
            {
                throw AppException.BadRequest("That saved card does not belong to the selected customer.");
            }
        }

        // The price comes from the product row, not from the browser.
        var transaction = new Transaction
        {
            CustomerId = customer.Id,
            ProductId = product.Id,
            InvoiceNo = InvoiceNumbers.Create(),
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = fingerprint,
            Amount = product.Price,
            Currency = "SGD",
            Status = TransactionStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        var inserted = await _transactions.TryAddAsync(transaction, cancellationToken);
        if (!inserted)
        {
            var winner = await _transactions.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
                ?? throw AppException.Conflict("Another checkout with this Idempotency-Key is still running. Wait for it to finish and submit again.");
            return Replay(winner, fingerprint);
        }

        var frontendReturnUrl = $"{_app.FrontendBaseUrl.TrimEnd('/')}/result/{transaction.InvoiceNo}";
        var backendReturnUrl = $"{_app.PublicApiBaseUrl.TrimEnd('/')}/api/payments/2c2p/notification";
        var description = product.Name.Length <= 250 ? product.Name : product.Name[..250];

        try
        {
            var response = card is null
                ? await PayOnHostedPageAsync(transaction, product, description, frontendReturnUrl, backendReturnUrl, cancellationToken)
                : await PayWithSavedCardAsync(transaction, customer, card, description, frontendReturnUrl, backendReturnUrl, cancellationToken);

            await RememberAsync(transaction, response, cancellationToken);
            return response;
        }
        catch (PaymentProviderException exception)
        {
            return await FailAsync(transaction, exception.RespCode, exception.Message, cancellationToken);
        }
        catch (AppException exception)
        {
            // Amount mismatches and similar problems happen after the row exists.
            // Store a failed response so the same key cannot call 2C2P again.
            return await FailAsync(transaction, null, exception.Message, cancellationToken);
        }
    }

    private async Task<CheckoutResponse> FailAsync(
        Transaction transaction,
        string? respCode,
        string message,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning("Checkout {InvoiceNo} failed: {Message}", transaction.InvoiceNo, message);
        await _transactions.TryFinalizeAsync(
            transaction.InvoiceNo,
            TransactionStatus.Failed.ToString(),
            null,
            null,
            null,
            TextLimits.Clip(respCode, 10),
            TextLimits.Clip(message, 255),
            null,
            cancellationToken);

        var failed = new CheckoutResponse(transaction.InvoiceNo, TransactionStatus.Failed.ToString(), null);
        await RememberAsync(transaction, failed, cancellationToken);
        return failed;
    }

    private static CheckoutResponse Replay(Transaction existing, string fingerprint)
    {
        if (!string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw AppException.IdempotencyMismatch();
        }

        if (string.IsNullOrWhiteSpace(existing.CheckoutResponseJson))
        {
            throw AppException.Conflict(
                "A checkout with this Idempotency-Key is still running. Wait for it to finish and submit again.");
        }

        return JsonSerializer.Deserialize<CheckoutResponse>(existing.CheckoutResponseJson, SnapshotJson)
            ?? throw AppException.Conflict("The original checkout response could not be read.");
    }

    private async Task RememberAsync(Transaction transaction, CheckoutResponse response, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(response, SnapshotJson);
        transaction.CheckoutResponseJson = json;
        await _transactions.SaveCheckoutSnapshotAsync(
            transaction.Id,
            transaction.PaymentToken,
            json,
            transaction.RespCode,
            transaction.RespDesc,
            cancellationToken);
    }

    private async Task<CheckoutResponse> PayOnHostedPageAsync(
        Transaction transaction,
        Product product,
        string description,
        string frontendReturnUrl,
        string backendReturnUrl,
        CancellationToken cancellationToken)
    {
        var recurring = product.PaymentType == PaymentType.Recurring;
        var token = await _gateway.CreateTokenAsync(new TokenCommand(
            transaction.InvoiceNo,
            description,
            transaction.Amount,
            frontendReturnUrl,
            backendReturnUrl,
            // OneTime leaves every channel the merchant enabled. Recurring is card-only,
            // and tokenize asks the hosted page to show the save-card checkbox.
            recurring ? new[] { "CC" } : null,
            Tokenize: recurring,
            CustomerToken: null,
            CustomerTokenOnly: false,
            Request3Ds: null), cancellationToken);

        transaction.PaymentToken = token.PaymentToken;
        transaction.RespCode = TextLimits.Clip(token.RespCode, 10);
        transaction.RespDesc = TextLimits.Clip(token.RespDesc, 255);

        if (string.IsNullOrWhiteSpace(token.WebPaymentUrl))
        {
            throw new PaymentProviderException(token.RespCode, "2C2P did not return a hosted payment page URL.");
        }

        return new CheckoutResponse(transaction.InvoiceNo, TransactionStatus.Pending.ToString(), token.WebPaymentUrl);
    }

    private async Task<CheckoutResponse> PayWithSavedCardAsync(
        Transaction transaction,
        Customer customer,
        CustomerCard card,
        string description,
        string frontendReturnUrl,
        string backendReturnUrl,
        CancellationToken cancellationToken)
    {
        var token = await _gateway.CreateTokenAsync(new TokenCommand(
            transaction.InvoiceNo,
            description,
            transaction.Amount,
            frontendReturnUrl,
            backendReturnUrl,
            PaymentChannel: new[] { "CC" },
            Tokenize: false,
            CustomerToken: new[] { card.CustomerToken },
            CustomerTokenOnly: true,
            // 2C2P names this field request3DS. N skips 3DS unless the bank still requires it.
            Request3Ds: "N"), cancellationToken);

        transaction.PaymentToken = token.PaymentToken;

        var payment = await _gateway.DoPaymentAsync(new DoPaymentCommand(
            token.PaymentToken,
            card.CustomerToken,
            customer.FullName,
            customer.Email,
            frontendReturnUrl), cancellationToken);

        transaction.RespCode = TextLimits.Clip(payment.RespCode, 10);
        transaction.RespDesc = TextLimits.Clip(payment.RespDesc, 255);

        if (payment.RespCode == "2000")
        {
            var inquiry = await _gateway.InquireAsync(transaction.InvoiceNo, cancellationToken);
            await _updates.ApplyVerifiedResultAsync(inquiry, isNotification: false, cancellationToken);
            var latest = await _transactions.GetResponseAsync(transaction.InvoiceNo, cancellationToken);
            var status = latest?.Status ?? TransactionStatus.Pending.ToString();
            return new CheckoutResponse(transaction.InvoiceNo, status, null);
        }

        if (ProviderResponseCodes.IsRedirect(payment.RespCode))
        {
            return new CheckoutResponse(
                transaction.InvoiceNo,
                TransactionStatus.Pending.ToString(),
                payment.RedirectUrl);
        }

        await _transactions.TryFinalizeAsync(
            transaction.InvoiceNo,
            TransactionStatus.Failed.ToString(),
            TextLimits.Clip(payment.ChannelCode, 20),
            null,
            null,
            TextLimits.Clip(payment.RespCode, 10),
            TextLimits.Clip(payment.RespDesc, 255),
            null,
            cancellationToken);

        return new CheckoutResponse(transaction.InvoiceNo, TransactionStatus.Failed.ToString(), null);
    }
}
