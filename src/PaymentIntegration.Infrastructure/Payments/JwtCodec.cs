using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PaymentIntegration.Application.Abstractions;

namespace PaymentIntegration.Infrastructure.Payments;

/// <summary>
/// Builds a JWT ourselves so the payload is exactly the JSON 2C2P expects.
/// A library token often adds exp/nbf claims that this API does not use.
/// Algorithm: HMAC SHA-256 with the merchant secret key.
/// </summary>
public sealed class JwtCodec
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly byte[] _secret;

    public JwtCodec(string secret)
    {
        _secret = Encoding.UTF8.GetBytes(secret);
    }

    public string Sign(object payload)
    {
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var body = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, Json));
        var unsigned = header + "." + body;
        return unsigned + "." + Base64UrlEncode(SignBytes(unsigned));
    }

    public T Read<T>(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            throw new InvalidPaymentSignatureException("JWT must have a header, payload, and signature.");
        }

        var actual = Base64UrlDecode(parts[2]);
        var expected = SignBytes(parts[0] + "." + parts[1]);
        if (actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            throw new InvalidPaymentSignatureException("JWT signature does not match the merchant secret.");
        }

        var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        return JsonSerializer.Deserialize<T>(json, Json)
            ?? throw new InvalidPaymentSignatureException("JWT payload was empty.");
    }

    private byte[] SignBytes(string unsigned)
    {
        using var hmac = new HMACSHA256(_secret);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2:
                padded += "==";
                break;
            case 3:
                padded += "=";
                break;
        }

        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            throw new InvalidPaymentSignatureException("JWT signature was not valid base64.");
        }
    }
}

/// <summary>Writes 48.00 rather than 48 so the amount 2C2P sees has two decimal places.</summary>
public sealed class MoneyJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture);
        }

        return reader.GetDecimal();
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToString("0.00", CultureInfo.InvariantCulture));
}

public sealed class NullableMoneyJsonConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return decimal.Parse(text, CultureInfo.InvariantCulture);
        }

        return reader.GetDecimal();
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteRawValue(value.Value.ToString("0.00", CultureInfo.InvariantCulture));
    }
}
