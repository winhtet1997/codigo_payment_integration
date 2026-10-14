namespace PaymentIntegration.Application.Abstractions;

/// <summary>
/// What we learn from a 2C2P notification or a Payment Inquiry after the JWT is verified.
/// </summary>
public sealed record PaymentResultNotice(
    string? InvoiceNo,
    decimal? Amount,
    string? CurrencyCode,
    string? RespCode,
    string? RespDesc,
    string? ChannelCode,
    string? AccountNo,
    string? TranRef,
    string? CustomerToken,
    decimal? FxAmount = null,
    string? FxCurrencyCode = null);

public sealed record TokenCommand(
    string InvoiceNo,
    string Description,
    decimal Amount,
    string FrontendReturnUrl,
    string BackendReturnUrl,
    IReadOnlyList<string>? PaymentChannel,
    bool Tokenize,
    IReadOnlyList<string>? CustomerToken,
    bool CustomerTokenOnly,
    string? Request3Ds);

public sealed record TokenResult(string PaymentToken, string? WebPaymentUrl, string RespCode, string RespDesc);

public sealed record DoPaymentCommand(
    string PaymentToken,
    string CustomerToken,
    string CustomerName,
    string Email,
    string ResponseReturnUrl);

public sealed record DoPaymentResult(string RespCode, string RespDesc, string? RedirectUrl, string? ChannelCode);

/// <summary>
/// The only door to 2C2P. A second payment provider would get its own class
/// behind this same interface, and CheckoutService would not change.
/// </summary>
public interface IPaymentGateway
{
    Task<TokenResult> CreateTokenAsync(TokenCommand command, CancellationToken cancellationToken);

    Task<DoPaymentResult> DoPaymentAsync(DoPaymentCommand command, CancellationToken cancellationToken);

    Task<PaymentResultNotice> InquireAsync(string invoiceNo, CancellationToken cancellationToken);
}

/// <summary>
/// Signs our requests and checks signatures on callbacks. Separate from the HTTP
/// client so tests can replace 2C2P calls and still use the real signature check.
/// </summary>
public interface ISignedPayloadReader
{
    PaymentResultNotice ReadNotification(string jwt);
}

/// <summary>
/// Thrown when 2C2P answers, but the answer is a definite failure (or we could not
/// reach them). CheckoutService stores that on the transaction and does not throw,
/// so a retry of the same idempotency key does not call 2C2P again.
/// </summary>
public sealed class InvalidPaymentSignatureException : Exception
{
    public InvalidPaymentSignatureException(string message) : base(message)
    {
    }
}

public sealed class PaymentProviderException : Exception
{
    public string? RespCode { get; }

    public PaymentProviderException(string? respCode, string message) : base(message)
    {
        RespCode = respCode;
    }
}
