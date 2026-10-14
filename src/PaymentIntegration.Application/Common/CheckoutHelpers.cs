using System.Security.Cryptography;

namespace PaymentIntegration.Application.Common;

public static class InvoiceNumbers
{
    public static string Create()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var suffix = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        return $"INV{stamp}{suffix}";
    }
}

public static class CheckoutFingerprint
{
    public static string Create(int customerId, int productId, int? customerCardId) =>
        $"{customerId}:{productId}:{customerCardId?.ToString() ?? "new"}";
}

public static class TextLimits
{
    public static string? Clip(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..max];
    }
}

public static class ProviderResponseCodes
{
    public static bool IsSuccess(string? code) => code == "0000";

    public static bool IsRedirect(string? code) =>
        code is "1000" or "1001" or "1002" or "1003" or "1004";

    public static bool IsStillPending(string? code) =>
        code is "0001" or "1000" or "1001" or "1002" or "1003" or "1004" or "1005" or "2001" or "2002";
}
