using System.Net;
using Xunit;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Infrastructure.Payments;

namespace PaymentIntegration.Tests;

[Collection(IntegrationCollection.Name)]
public sealed class CheckoutTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CheckoutApiFixture _fixture;

    public CheckoutTests(CheckoutApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Gateway.Reset();
    }

    [Fact]
    public async Task Ten_parallel_checkouts_with_one_key_create_one_transaction_and_one_provider_call()
    {
        var customer = await FirstCustomerAsync();
        var product = await FirstProductAsync("OneTime");
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PostCheckoutAsync(customer.Id, product.Id, null, key)));

        var bodies = new List<string>();
        foreach (var response in responses)
        {
            bodies.Add(await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var invoices = bodies
            .Select(body => JsonSerializer.Deserialize<CheckoutBody>(body, Json)!.InvoiceNo)
            .Distinct()
            .ToList();

        Assert.Single(invoices);
        Assert.Equal(1, _fixture.Gateway.TokenCalls);
        Assert.Equal(0, _fixture.Gateway.DoPaymentCalls);

        await using var db = _fixture.CreateDb();
        var rows = await db.Transactions.CountAsync(transaction => transaction.IdempotencyKey == key);
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task Repeating_the_same_key_does_not_call_the_provider_again()
    {
        var customer = await FirstCustomerAsync();
        var product = await FirstProductAsync("OneTime");
        var key = Guid.NewGuid().ToString();

        var first = await PostCheckoutAsync(customer.Id, product.Id, null, key);
        var second = await PostCheckoutAsync(customer.Id, product.Id, null, key);
        var firstBody = await first.Content.ReadFromJsonAsync<CheckoutBody>(Json);
        var secondBody = await second.Content.ReadFromJsonAsync<CheckoutBody>(Json);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstBody!.InvoiceNo, secondBody!.InvoiceNo);
        Assert.Equal(firstBody.RedirectUrl, secondBody.RedirectUrl);
        Assert.Equal(1, _fixture.Gateway.TokenCalls);

        var token = Assert.Single(_fixture.Gateway.TokenCommands);
        Assert.Null(token.PaymentChannel);
        Assert.False(token.Tokenize);
        Assert.Null(token.CustomerToken);
        Assert.False(token.CustomerTokenOnly);
        Assert.Null(token.Request3Ds);
        Assert.Contains(firstBody.InvoiceNo, token.FrontendReturnUrl);
        Assert.EndsWith("/api/payments/2c2p/notification", token.BackendReturnUrl);
    }

    [Fact]
    public async Task Recurring_new_card_requests_card_channel_and_tokenize()
    {
        var customer = await FirstCustomerAsync();
        var product = await FirstProductAsync("Recurring");
        var response = await PostCheckoutAsync(customer.Id, product.Id, null, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token = Assert.Single(_fixture.Gateway.TokenCommands);
        Assert.Equal(new[] { "CC" }, token.PaymentChannel);
        Assert.True(token.Tokenize);
        Assert.Null(token.CustomerToken);
        Assert.False(token.CustomerTokenOnly);
        Assert.Equal(0, _fixture.Gateway.DoPaymentCalls);
    }

    [Fact]
    public async Task Missing_idempotency_key_is_rejected()
    {
        var customer = await FirstCustomerAsync();
        var product = await FirstProductAsync("OneTime");
        var response = await PostCheckoutAsync(customer.Id, product.Id, null, "");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _fixture.Gateway.TokenCalls);
    }

    [Fact]
    public async Task Unknown_customer_is_not_found()
    {
        var product = await FirstProductAsync("OneTime");
        var response = await PostCheckoutAsync(99999, product.Id, null, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, _fixture.Gateway.TokenCalls);
    }

    [Fact]
    public async Task Notification_is_idempotent_and_saves_the_card_once()
    {
        var customer = await FirstCustomerAsync();
        var product = await FirstProductAsync("OneTime");
        var checkout = await PostCheckoutAsync(customer.Id, product.Id, null, Guid.NewGuid().ToString());
        var created = await checkout.Content.ReadFromJsonAsync<CheckoutBody>(Json);
        var detail = await _fixture.Client.GetFromJsonAsync<TransactionBody>($"/api/transactions/{created!.InvoiceNo}", Json);

        var payload = new JwtCodec(CheckoutApiFixture.Secret).Sign(new Dictionary<string, object?>
        {
            ["invoiceNo"] = created.InvoiceNo,
            ["amount"] = detail!.Amount,
            ["currencyCode"] = "SGD",
            ["channelCode"] = "CC",
            ["accountNo"] = "411111XXXXXX1111",
            ["tranRef"] = "TRX100",
            ["customerToken"] = "cust-token-" + created.InvoiceNo,
            ["respCode"] = "0000",
            ["respDesc"] = "Success"
        });

        var calls = Enumerable.Range(0, 10).Select(_ =>
            _fixture.Client.PostAsJsonAsync("/api/payments/2c2p/notification", new { payload }));
        var notifications = await Task.WhenAll(calls);
        Assert.All(notifications, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        detail = await _fixture.Client.GetFromJsonAsync<TransactionBody>($"/api/transactions/{created.InvoiceNo}", Json);
        Assert.Equal("Succeeded", detail!.Status);
        Assert.Equal("411111XXXXXX1111", detail.MaskedCardNo);
        Assert.Equal("CC", detail.PaymentChannel);

        await using (var db = _fixture.CreateDb())
        {
            var saved = await db.CustomerCards.SingleAsync(card => card.CustomerToken == "cust-token-" + created.InvoiceNo);
            var cards = await _fixture.Client.GetFromJsonAsync<List<CardBody>>($"/api/customers/{customer.Id}/cards", Json);
            Assert.Contains(cards!, card => card.Id == saved.Id && card.MaskedCardNo == "411111XXXXXX1111");
        }

        var failure = new JwtCodec(CheckoutApiFixture.Secret).Sign(new Dictionary<string, object?>
        {
            ["invoiceNo"] = created.InvoiceNo,
            ["amount"] = detail.Amount,
            ["currencyCode"] = "SGD",
            ["respCode"] = "0003",
            ["respDesc"] = "Payment is cancelled"
        });
        var ignored = await _fixture.Client.PostAsJsonAsync("/api/payments/2c2p/notification", new { payload = failure });
        Assert.Equal(HttpStatusCode.OK, ignored.StatusCode);

        detail = await _fixture.Client.GetFromJsonAsync<TransactionBody>($"/api/transactions/{created.InvoiceNo}", Json);
        Assert.Equal("Succeeded", detail!.Status);
        await using (var db = _fixture.CreateDb())
        {
            Assert.Equal(1, await db.CustomerCards.CountAsync(card => card.CustomerToken == "cust-token-" + created.InvoiceNo));
        }
    }

    [Fact]
    public async Task Bad_signature_and_amount_mismatch_do_not_change_the_payment()
    {
        var customer = await FirstCustomerAsync();
        var product = await FirstProductAsync("OneTime");
        var checkout = await PostCheckoutAsync(customer.Id, product.Id, null, Guid.NewGuid().ToString());
        var created = await checkout.Content.ReadFromJsonAsync<CheckoutBody>(Json);

        var tampered = new JwtCodec(CheckoutApiFixture.Secret).Sign(new Dictionary<string, object?>
        {
            ["invoiceNo"] = created!.InvoiceNo,
            ["amount"] = 1.00m,
            ["currencyCode"] = "SGD",
            ["respCode"] = "0000",
            ["respDesc"] = "Success"
        });
        var mismatch = await _fixture.Client.PostAsJsonAsync("/api/payments/2c2p/notification", new { payload = tampered });
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);

        var signed = new JwtCodec(CheckoutApiFixture.Secret).Sign(new Dictionary<string, object?>
        {
            ["invoiceNo"] = created.InvoiceNo,
            ["amount"] = product.Price,
            ["currencyCode"] = "SGD",
            ["respCode"] = "0000",
            ["respDesc"] = "Success"
        });
        var broken = signed[..^1] + (signed[^1] == 'a' ? "b" : "a");
        var badSignature = await _fixture.Client.PostAsJsonAsync("/api/payments/2c2p/notification", new { payload = broken });
        Assert.Equal(HttpStatusCode.BadRequest, badSignature.StatusCode);

        var detail = await _fixture.Client.GetFromJsonAsync<TransactionBody>($"/api/transactions/{created.InvoiceNo}", Json);
        Assert.Equal("Pending", detail!.Status);
    }

    [Fact]
    public async Task Settlement_currency_can_differ_when_the_fx_amount_matches()
    {
        var customer = await FirstCustomerAsync();
        var product = await FirstProductAsync("OneTime");
        var checkout = await PostCheckoutAsync(customer.Id, product.Id, null, Guid.NewGuid().ToString());
        var created = await checkout.Content.ReadFromJsonAsync<CheckoutBody>(Json);
        var detail = await _fixture.Client.GetFromJsonAsync<TransactionBody>($"/api/transactions/{created!.InvoiceNo}", Json);

        var payload = new JwtCodec(CheckoutApiFixture.Secret).Sign(new Dictionary<string, object?>
        {
            ["invoiceNo"] = created.InvoiceNo,
            ["amount"] = 1163.30m,
            ["currencyCode"] = "THB",
            ["fxAmount"] = detail!.Amount,
            ["fxCurrencyCode"] = "SGD",
            ["channelCode"] = "VI",
            ["accountNo"] = "411111XXXXXX1111",
            ["tranRef"] = "TRXFX",
            ["respCode"] = "0000",
            ["respDesc"] = "Success"
        });

        var response = await _fixture.Client.PostAsJsonAsync("/api/payments/2c2p/notification", new { payload });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        detail = await _fixture.Client.GetFromJsonAsync<TransactionBody>($"/api/transactions/{created.InvoiceNo}", Json);
        Assert.Equal("Succeeded", detail!.Status);
        Assert.Equal("411111XXXXXX1111", detail.MaskedCardNo);
    }

    [Fact]
    public async Task Saved_card_payment_uses_do_payment_and_inquiry()
    {
        var customer = await FirstCustomerAsync();
        var oneTime = await FirstProductAsync("OneTime");
        var recurring = await FirstProductAsync("Recurring");
        var seeded = await PostCheckoutAsync(customer.Id, oneTime.Id, null, Guid.NewGuid().ToString());
        var created = await seeded.Content.ReadFromJsonAsync<CheckoutBody>(Json);
        var detail = await _fixture.Client.GetFromJsonAsync<TransactionBody>($"/api/transactions/{created!.InvoiceNo}", Json);
        var token = "saved-" + Guid.NewGuid().ToString("N");
        var payload = new JwtCodec(CheckoutApiFixture.Secret).Sign(new Dictionary<string, object?>
        {
            ["invoiceNo"] = created.InvoiceNo,
            ["amount"] = detail!.Amount,
            ["currencyCode"] = "SGD",
            ["accountNo"] = "555555XXXXXX4444",
            ["customerToken"] = token,
            ["channelCode"] = "CC",
            ["tranRef"] = "TRX200",
            ["respCode"] = "0000",
            ["respDesc"] = "Success"
        });
        Assert.Equal(HttpStatusCode.OK, (await _fixture.Client.PostAsJsonAsync("/api/payments/2c2p/notification", new { payload })).StatusCode);

        int cardId;
        await using (var db = _fixture.CreateDb())
        {
            cardId = await db.CustomerCards.Where(card => card.CustomerToken == token).Select(card => card.Id).SingleAsync();
        }
        var cards = await _fixture.Client.GetFromJsonAsync<List<CardBody>>($"/api/customers/{customer.Id}/cards", Json);
        var card = Assert.Single(cards!, item => item.Id == cardId);
        Assert.Equal("555555XXXXXX4444", card.MaskedCardNo);

        _fixture.Gateway.Reset();
        var paid = await PostCheckoutAsync(customer.Id, recurring.Id, card.Id, Guid.NewGuid().ToString());
        var body = await paid.Content.ReadFromJsonAsync<CheckoutBody>(Json);

        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal("Succeeded", body!.Status);
        Assert.Null(body.RedirectUrl);
        Assert.Equal(1, _fixture.Gateway.TokenCalls);
        Assert.Equal(1, _fixture.Gateway.DoPaymentCalls);
        Assert.Equal(1, _fixture.Gateway.InquiryCalls);

        var tokenRequest = Assert.Single(_fixture.Gateway.TokenCommands);
        Assert.Equal(new[] { "CC" }, tokenRequest.PaymentChannel);
        Assert.Equal(new[] { token }, tokenRequest.CustomerToken);
        Assert.True(tokenRequest.CustomerTokenOnly);
        Assert.False(tokenRequest.Tokenize);
        Assert.Equal("N", tokenRequest.Request3Ds);

        var savedPayment = await _fixture.Client.GetFromJsonAsync<TransactionBody>($"/api/transactions/{body.InvoiceNo}", Json);
        Assert.Equal("0000", savedPayment!.RespCode);
        Assert.Equal("Succeeded", savedPayment.Status);
    }

    private async Task<CustomerBody> FirstCustomerAsync()
    {
        var customers = await _fixture.Client.GetFromJsonAsync<List<CustomerBody>>("/api/customers", Json);
        return customers!.OrderBy(customer => customer.Id).First();
    }

    private async Task<ProductBody> FirstProductAsync(string paymentType)
    {
        var products = await _fixture.Client.GetFromJsonAsync<List<ProductBody>>("/api/products", Json);
        return products!.OrderBy(product => product.Id).First(product => product.PaymentType == paymentType);
    }

    private Task<HttpResponseMessage> PostCheckoutAsync(int customerId, int productId, int? cardId, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkout");
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        var json = JsonSerializer.Serialize(new { customerId, productId, customerCardId = cardId });
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return _fixture.Client.SendAsync(request);
    }

    private sealed record CustomerBody(int Id, string FirstName, string LastName, string Email);

    private sealed record ProductBody(int Id, string Name, decimal Price, string PaymentType);

    private sealed record CheckoutBody(string InvoiceNo, string Status, string? RedirectUrl);

    private sealed record TransactionBody(
        string InvoiceNo,
        string Status,
        string? RespCode,
        decimal Amount,
        string? PaymentChannel,
        string? MaskedCardNo);

    private sealed record CardBody(int Id, string MaskedCardNo);
}
