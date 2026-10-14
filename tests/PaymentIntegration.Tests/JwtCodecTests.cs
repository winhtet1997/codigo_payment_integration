using System.Text.Json;
using Xunit;
using PaymentIntegration.Application.Abstractions;
using PaymentIntegration.Infrastructure.Payments;

namespace PaymentIntegration.Tests;

public sealed class JwtCodecTests
{
    [Fact]
    public void Round_trip_keeps_the_payload_and_rejects_a_tampered_token()
    {
        var codec = new JwtCodec("merchant-secret");
        var token = codec.Sign(new Dictionary<string, object?>
        {
            ["invoiceNo"] = "INV1",
            ["respCode"] = "0000"
        });

        var payload = codec.Read<JsonElement>(token);
        Assert.Equal("INV1", payload.GetProperty("invoiceNo").GetString());
        Assert.Equal("0000", payload.GetProperty("respCode").GetString());

        var otherSecret = new JwtCodec("other-secret");
        Assert.Throws<InvalidPaymentSignatureException>(() => otherSecret.Read<JsonElement>(token));

        var broken = token[..^1] + (token[^1] == 'a' ? "b" : "a");
        Assert.Throws<InvalidPaymentSignatureException>(() => codec.Read<JsonElement>(broken));
    }
}
