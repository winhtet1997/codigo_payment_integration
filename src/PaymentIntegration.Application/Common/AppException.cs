namespace PaymentIntegration.Application.Common;

/// <summary>
/// An expected failure we can show to the caller as RFC 7807 problem details.
/// Unexpected bugs are not this type; those become a generic HTTP 500.
/// </summary>
public sealed class AppException : Exception
{
    public int StatusCode { get; }

    public string Title { get; }

    public AppException(int statusCode, string title, string detail)
        : base(detail)
    {
        StatusCode = statusCode;
        Title = title;
    }

    public static AppException BadRequest(string detail) =>
        new(400, "Invalid request", detail);

    public static AppException NotFound(string detail) =>
        new(404, "Not found", detail);

    public static AppException Conflict(string detail) =>
        new(409, "Checkout already in progress", detail);

    public static AppException IdempotencyMismatch() =>
        new(422, "Idempotency key reused",
            "This Idempotency-Key was already used for a different customer, product, or card. Reload the page to start a new checkout.");

    public static AppException Unavailable(string detail) =>
        new(503, "Service unavailable", detail);
}
