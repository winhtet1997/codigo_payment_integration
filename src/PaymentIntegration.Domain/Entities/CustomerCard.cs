namespace PaymentIntegration.Domain.Entities;

/// <summary>
/// A card 2C2P has already tokenized. We store their customerToken and a masked
/// number for the dropdown. Never a full PAN and never a CVV.
/// </summary>
public sealed class CustomerCard
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public string CustomerToken { get; set; } = "";

    public string MaskedCardNo { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public Customer? Customer { get; set; }
}
