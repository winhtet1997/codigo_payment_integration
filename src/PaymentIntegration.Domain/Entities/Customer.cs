namespace PaymentIntegration.Domain.Entities;

public sealed class Customer
{
    public int Id { get; set; }

    public string FirstName { get; set; } = "";

    public string LastName { get; set; } = "";

    public string Email { get; set; } = "";

    public string FullName => $"{FirstName} {LastName}".Trim();
}
