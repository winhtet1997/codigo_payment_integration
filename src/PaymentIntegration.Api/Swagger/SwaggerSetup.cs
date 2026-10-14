using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PaymentIntegration.Api.Swagger;

public static class SwaggerSetup
{
    public static void Configure(SwaggerGenOptions options)
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Payment checkout API",
            Version = "v1",
            Description = """
                Checkout for the 2C2P sandbox. Singapore, SGD.
                New payment methods use the hosted page. Saved cards use Do Payment and then Payment Inquiry.
                Send an Idempotency-Key header on POST /api/checkout. Reuse it to retry the same click. Generate a new one for a new purchase.
                """
        });

        options.OperationFilter<CheckoutExamplesFilter>();

        var xml = Path.Combine(AppContext.BaseDirectory, "PaymentIntegration.Api.xml");
        if (File.Exists(xml))
        {
            options.IncludeXmlComments(xml);
        }
    }
}

public sealed class CheckoutExamplesFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        switch (context.MethodInfo.Name)
        {
            case "Checkout":
                DescribeIdempotencyHeader(operation);
                SetRequestExample(operation, new OpenApiObject
                {
                    ["customerId"] = new OpenApiInteger(1),
                    ["productId"] = new OpenApiInteger(1),
                    ["customerCardId"] = new OpenApiNull()
                });
                SetJsonExample(operation, "200", new OpenApiObject
                {
                    ["invoiceNo"] = new OpenApiString("INV20261007000123"),
                    ["status"] = new OpenApiString("Pending"),
                    ["redirectUrl"] = new OpenApiString("https://sandbox-pgw-ui.2c2p.com/payment/4.3/#/token/example")
                });
                SetJsonExample(operation, "400", Problem("Invalid request", 400, "The Idempotency-Key header is required and must be at most 100 characters."));
                SetJsonExample(operation, "404", Problem("Not found", 404, "Customer 99 was not found."));
                SetJsonExample(operation, "409", Problem("Checkout already in progress", 409, "Another checkout with this Idempotency-Key is still running."));
                SetJsonExample(operation, "422", Problem("Idempotency key reused", 422, "This Idempotency-Key was already used for a different customer, product, or card."));
                break;
            case "List" when context.MethodInfo.DeclaringType?.Name == "CustomersController":
                SetJsonExample(operation, "200", new OpenApiArray
                {
                    new OpenApiObject
                    {
                        ["id"] = new OpenApiInteger(1),
                        ["firstName"] = new OpenApiString("Aung"),
                        ["lastName"] = new OpenApiString("Min"),
                        ["email"] = new OpenApiString("aung.min@example.com")
                    }
                });
                break;
            case "List":
                SetJsonExample(operation, "200", new OpenApiArray
                {
                    new OpenApiObject
                    {
                        ["id"] = new OpenApiInteger(1),
                        ["name"] = new OpenApiString("City Tour"),
                        ["description"] = new OpenApiString("A half-day walking tour of Singapore."),
                        ["price"] = new OpenApiDouble(48.00),
                        ["currency"] = new OpenApiString("SGD"),
                        ["paymentType"] = new OpenApiString("OneTime")
                    }
                });
                break;
            case "Cards":
                SetJsonExample(operation, "200", new OpenApiArray
                {
                    new OpenApiObject
                    {
                        ["id"] = new OpenApiInteger(1),
                        ["maskedCardNo"] = new OpenApiString("411111XXXXXX1111"),
                        ["createdAt"] = new OpenApiString("2026-10-09T07:30:00")
                    }
                });
                break;
            case "Notification":
                SetRequestExample(operation, new OpenApiObject
                {
                    ["payload"] = new OpenApiString("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpbnZvaWNlTm8iOiJJTlYyMDI2MTAwNzAwMDEyMyJ9.signature")
                });
                SetJsonExample(operation, "200", new OpenApiObject
                {
                    ["received"] = new OpenApiBoolean(true)
                });
                SetJsonExample(operation, "400", Problem("Invalid notification", 400, "The notification signature could not be verified."));
                break;
            case "GetTransaction":
            case "Inquire":
                SetJsonExample(operation, "200", TransactionExample());
                SetJsonExample(operation, "404", Problem("Not found", 404, "No payment was found for invoice INV20261007000123."));
                break;
        }
    }

    private static void DescribeIdempotencyHeader(OpenApiOperation operation)
    {
        operation.Parameters ??= new List<OpenApiParameter>();
        var header = operation.Parameters.FirstOrDefault(parameter =>
            parameter.In == ParameterLocation.Header &&
            string.Equals(parameter.Name, "Idempotency-Key", StringComparison.OrdinalIgnoreCase));

        if (header is null)
        {
            header = new OpenApiParameter
            {
                Name = "Idempotency-Key",
                In = ParameterLocation.Header,
                Schema = new OpenApiSchema { Type = "string", Format = "uuid" }
            };
            operation.Parameters.Add(header);
        }

        header.Required = true;
        header.Description = "UUID for this purchase. Send the same value if the request is retried. A different customer, product, or card needs a new value.";
        header.Schema ??= new OpenApiSchema { Type = "string", Format = "uuid" };
        header.Example = new OpenApiString("3f1c2c3e-7b2a-4e5d-9c1a-8b7d6e5f4a3b");
    }

    private static OpenApiObject TransactionExample() => new()
    {
        ["invoiceNo"] = new OpenApiString("INV20261007000123"),
        ["status"] = new OpenApiString("Succeeded"),
        ["respCode"] = new OpenApiString("0000"),
        ["respDesc"] = new OpenApiString("Success"),
        ["amount"] = new OpenApiDouble(48.00),
        ["currency"] = new OpenApiString("SGD"),
        ["productName"] = new OpenApiString("City Tour"),
        ["paymentType"] = new OpenApiString("OneTime"),
        ["customerId"] = new OpenApiInteger(1),
        ["productId"] = new OpenApiInteger(1),
        ["paymentChannel"] = new OpenApiString("CC"),
        ["maskedCardNo"] = new OpenApiString("411111XXXXXX1111"),
        ["paidAt"] = new OpenApiString("2026-10-09T07:30:00")
    };

    private static OpenApiObject Problem(string title, int status, string detail) => new()
    {
        ["type"] = new OpenApiString("about:blank"),
        ["title"] = new OpenApiString(title),
        ["status"] = new OpenApiInteger(status),
        ["detail"] = new OpenApiString(detail)
    };

    private static void SetRequestExample(OpenApiOperation operation, IOpenApiAny example)
    {
        operation.RequestBody ??= new OpenApiRequestBody { Required = true };
        if (!operation.RequestBody.Content.TryGetValue("application/json", out var media))
        {
            media = new OpenApiMediaType();
            operation.RequestBody.Content["application/json"] = media;
        }

        media.Example = example;
    }

    private static void SetJsonExample(OpenApiOperation operation, string status, IOpenApiAny example)
    {
        if (!operation.Responses.TryGetValue(status, out var response))
        {
            response = new OpenApiResponse { Description = status };
            operation.Responses[status] = response;
        }

        var contentType = status == "200" ? "application/json" : "application/problem+json";
        if (!response.Content.TryGetValue(contentType, out var media))
        {
            media = new OpenApiMediaType();
            response.Content[contentType] = media;
        }

        media.Example = example;
    }
}
