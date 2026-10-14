namespace PaymentIntegration.Domain.Enums;

/// <summary>
/// OneTime is a normal purchase. Recurring only means we ask 2C2P to save the card.
/// This app does not schedule later charges.
/// </summary>
public enum PaymentType
{
    OneTime = 1,
    Recurring = 2
}
