using PaymentIntegration.Domain.Enums;

namespace PaymentIntegration.Domain.Entities;

public sealed class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public decimal Price { get; set; }

    public string Currency { get; set; } = "SGD";

    public PaymentType PaymentType { get; set; }
}
