namespace PaymentIntegration.Application.Contracts;

public sealed record CustomerResponse(int Id, string FirstName, string LastName, string Email);

public sealed record ProductResponse(
    int Id,
    string Name,
    string Description,
    decimal Price,
    string Currency,
    string PaymentType);

/// <summary>Masked number only. The customerToken stays on the server.</summary>
public sealed record SavedCardResponse(int Id, string MaskedCardNo, DateTime CreatedAt);

public sealed record CheckoutResponse(string InvoiceNo, string Status, string? RedirectUrl);

public sealed record TransactionResponse(
    string InvoiceNo,
    string Status,
    string? RespCode,
    string? RespDesc,
    decimal Amount,
    string Currency,
    string ProductName,
    string PaymentType,
    int CustomerId,
    int ProductId,
    string? PaymentChannel,
    string? MaskedCardNo,
    DateTime? PaidAt);

public sealed record CheckoutCommand(int CustomerId, int ProductId, int? CustomerCardId, string IdempotencyKey);
