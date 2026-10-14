using System.Text.Json.Serialization;
using PaymentIntegration.Application.Abstractions;

namespace PaymentIntegration.Infrastructure.Payments;

public sealed class TwoC2POptions
{
    public const string SectionName = "TwoC2P";

    public string MerchantId { get; set; } = "";

    public string SecretKey { get; set; } = "";

    public string PaymentTokenUrl { get; set; } = "https://sandbox-pgw.2c2p.com/payment/4.3/paymentToken";

    public string DoPaymentUrl { get; set; } = "https://sandbox-pgw.2c2p.com/payment/4.3/payment";

    public string InquiryUrl { get; set; } = "https://sandbox-pgw.2c2p.com/payment/4.3/paymentInquiry";
}

public sealed class PaymentTokenWire
{
    [JsonPropertyName("merchantID")]
    public string MerchantId { get; set; } = "";

    [JsonPropertyName("invoiceNo")]
    public string InvoiceNo { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("amount")]
    [JsonConverter(typeof(MoneyJsonConverter))]
    public decimal Amount { get; set; }

    [JsonPropertyName("currencyCode")]
    public string CurrencyCode { get; set; } = "SGD";

    [JsonPropertyName("frontendReturnUrl")]
    public string FrontendReturnUrl { get; set; } = "";

    [JsonPropertyName("backendReturnUrl")]
    public string BackendReturnUrl { get; set; } = "";

    [JsonPropertyName("paymentChannel")]
    public IReadOnlyList<string>? PaymentChannel { get; set; }

    [JsonPropertyName("tokenize")]
    public bool? Tokenize { get; set; }

    [JsonPropertyName("customerToken")]
    public IReadOnlyList<string>? CustomerToken { get; set; }

    [JsonPropertyName("customerTokenOnly")]
    public bool? CustomerTokenOnly { get; set; }

    [JsonPropertyName("request3DS")]
    public string? Request3Ds { get; set; }
}

public sealed class PaymentTokenResponseWire
{
    [JsonPropertyName("webPaymentUrl")]
    public string? WebPaymentUrl { get; set; }

    [JsonPropertyName("paymentToken")]
    public string? PaymentToken { get; set; }

    [JsonPropertyName("respCode")]
    public string? RespCode { get; set; }

    [JsonPropertyName("respDesc")]
    public string? RespDesc { get; set; }
}

public sealed class DoPaymentWire
{
    [JsonPropertyName("paymentToken")]
    public string PaymentToken { get; set; } = "";

    [JsonPropertyName("locale")]
    public string Locale { get; set; } = "en";

    [JsonPropertyName("responseReturnUrl")]
    public string ResponseReturnUrl { get; set; } = "";

    [JsonPropertyName("payment")]
    public DoPaymentBody Payment { get; set; } = new();
}

public sealed class DoPaymentBody
{
    [JsonPropertyName("code")]
    public DoPaymentCode Code { get; set; } = new();

    [JsonPropertyName("data")]
    public DoPaymentData Data { get; set; } = new();
}

public sealed class DoPaymentCode
{
    [JsonPropertyName("channelCode")]
    public string ChannelCode { get; set; } = "CC";
}

public sealed class DoPaymentData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    [JsonPropertyName("customerToken")]
    public string CustomerToken { get; set; } = "";
}

public sealed class InquiryWire
{
    [JsonPropertyName("merchantID")]
    public string MerchantId { get; set; } = "";

    [JsonPropertyName("invoiceNo")]
    public string InvoiceNo { get; set; } = "";

    [JsonPropertyName("locale")]
    public string Locale { get; set; } = "en";
}

public sealed class ProviderResultWire
{
    [JsonPropertyName("invoiceNo")]
    public string? InvoiceNo { get; set; }

    [JsonPropertyName("amount")]
    [JsonConverter(typeof(NullableMoneyJsonConverter))]
    public decimal? Amount { get; set; }

    [JsonPropertyName("currencyCode")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("respCode")]
    public string? RespCode { get; set; }

    [JsonPropertyName("respDesc")]
    public string? RespDesc { get; set; }

    [JsonPropertyName("channelCode")]
    public string? ChannelCode { get; set; }

    [JsonPropertyName("accountNo")]
    public string? AccountNo { get; set; }

    [JsonPropertyName("tranRef")]
    public string? TranRef { get; set; }

    [JsonPropertyName("customerToken")]
    public string? CustomerToken { get; set; }

    [JsonPropertyName("fxAmount")]
    [JsonConverter(typeof(NullableMoneyJsonConverter))]
    public decimal? FxAmount { get; set; }

    [JsonPropertyName("fxCurrencyCode")]
    public string? FxCurrencyCode { get; set; }

    public PaymentResultNotice ToNotice() => new(
        InvoiceNo,
        Amount,
        CurrencyCode,
        RespCode,
        RespDesc,
        ChannelCode,
        AccountNo,
        TranRef,
        CustomerToken,
        FxAmount,
        FxCurrencyCode);
}
