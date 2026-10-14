using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentIntegration.Application.Abstractions;

namespace PaymentIntegration.Infrastructure.Payments;

public sealed class TwoC2PGateway : IPaymentGateway
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly TwoC2POptions _options;
    private readonly JwtCodec _jwt;
    private readonly ILogger<TwoC2PGateway> _logger;

    public TwoC2PGateway(HttpClient http, IOptions<TwoC2POptions> options, ILogger<TwoC2PGateway> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _jwt = new JwtCodec(_options.SecretKey);
    }

    public async Task<TokenResult> CreateTokenAsync(TokenCommand command, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var wire = new PaymentTokenWire
        {
            MerchantId = _options.MerchantId,
            InvoiceNo = command.InvoiceNo,
            Description = command.Description,
            Amount = command.Amount,
            CurrencyCode = "SGD",
            FrontendReturnUrl = command.FrontendReturnUrl,
            BackendReturnUrl = command.BackendReturnUrl,
            PaymentChannel = command.PaymentChannel,
            Tokenize = command.Tokenize ? true : null,
            CustomerToken = command.CustomerToken,
            CustomerTokenOnly = command.CustomerTokenOnly ? true : null,
            Request3Ds = command.Request3Ds
        };

        using var decodedDocument = await PostJwtAsync(_options.PaymentTokenUrl, wire, cancellationToken);
        var decoded = decodedDocument.Deserialize<PaymentTokenResponseWire>(Json) ?? new PaymentTokenResponseWire();
        if (decoded.RespCode != "0000" || string.IsNullOrWhiteSpace(decoded.PaymentToken))
        {
            throw new PaymentProviderException(
                decoded.RespCode,
                string.IsNullOrWhiteSpace(decoded.RespDesc) ? "2C2P did not issue a payment token." : decoded.RespDesc);
        }

        return new TokenResult(decoded.PaymentToken, decoded.WebPaymentUrl, decoded.RespCode ?? "0000", decoded.RespDesc ?? "Success");
    }

    public async Task<DoPaymentResult> DoPaymentAsync(DoPaymentCommand command, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var name = command.CustomerName.Trim();
        if (name.Length > 50)
        {
            name = name[..50];
        }

        var email = command.Email.Trim();
        if (email.Length > 150)
        {
            email = email[..150];
        }

        // Do Payment is plain JSON. It is not a JWT.
        var body = new DoPaymentWire
        {
            PaymentToken = command.PaymentToken,
            Locale = "en",
            ResponseReturnUrl = command.ResponseReturnUrl,
            Payment = new DoPaymentBody
            {
                Code = new DoPaymentCode { ChannelCode = "CC" },
                Data = new DoPaymentData
                {
                    Name = name,
                    Email = email,
                    CustomerToken = command.CustomerToken
                }
            }
        };

        using var response = await _http.PostAsJsonAsync(_options.DoPaymentUrl, body, Json, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        var respCode = ReadString(document, "respCode") ?? "";
        var respDesc = ReadString(document, "respDesc") ?? "2C2P did not describe the Do Payment result.";
        var channel = ReadString(document, "channelCode");
        var redirect = ReadString(document, "data");

        _logger.LogInformation("Do Payment for token ending {Suffix} returned {RespCode}.", Suffix(command.PaymentToken), respCode);
        return new DoPaymentResult(respCode, respDesc, redirect, channel);
    }

    public async Task<PaymentResultNotice> InquireAsync(string invoiceNo, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var wire = new InquiryWire
        {
            MerchantId = _options.MerchantId,
            InvoiceNo = invoiceNo,
            Locale = "en"
        };

        using var decodedDocument = await PostJwtAsync(_options.InquiryUrl, wire, cancellationToken);
        var decoded = decodedDocument.Deserialize<ProviderResultWire>(Json) ?? new ProviderResultWire();
        if (string.IsNullOrWhiteSpace(decoded.InvoiceNo))
        {
            decoded.InvoiceNo = invoiceNo;
        }

        return decoded.ToNotice();
    }

    private async Task<JsonDocument> PostJwtAsync(string url, object payload, CancellationToken cancellationToken)
    {
        var token = _jwt.Sign(payload);
        using var response = await _http.PostAsJsonAsync(url, new JwtEnvelope(token), Json, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        var responseToken = ReadString(document, "payload");
        if (string.IsNullOrWhiteSpace(responseToken))
        {
            var respDesc = ReadString(document, "respDesc");
            throw new PaymentProviderException(ReadString(document, "respCode"), respDesc ?? "2C2P returned an empty payload.");
        }

        var json = JsonSerializer.Serialize(_jwt.Read<JsonElement>(responseToken));
        return JsonDocument.Parse(json);
    }

    private async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("2C2P HTTP {Status}: {Body}", (int)response.StatusCode, text);
            throw new PaymentProviderException(null, $"2C2P returned HTTP {(int)response.StatusCode}.");
        }

        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw new PaymentProviderException(null, "2C2P returned a response that was not JSON.");
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.MerchantId) || string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            throw new PaymentProviderException(
                null,
                "2C2P merchant ID and secret key are not configured. Copy appsettings.Development.example.json and fill in the sandbox keys.");
        }
    }

    private static string? ReadString(JsonDocument document, string name)
    {
        if (!document.RootElement.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static string Suffix(string value) => value.Length <= 6 ? value : value[^6..];

    private sealed record JwtEnvelope([property: JsonPropertyName("payload")] string Payload);
}
