using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PaymentIntegration.Domain.Entities;
using PaymentIntegration.Domain.Enums;

namespace PaymentIntegration.Infrastructure.Persistence;

public sealed class DatabaseInitializer
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(IServiceScopeFactory scopes, ILogger<DatabaseInitializer> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        const int attempts = 30;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync(cancellationToken);
                await SeedAsync(db, cancellationToken);
                return;
            }
            catch (Exception exception) when (attempt < attempts)
            {
                _logger.LogWarning(exception, "Database is not ready (attempt {Attempt} of {Attempts}).", attempt, attempts);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    private static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Customers.AnyAsync(cancellationToken))
        {
            return;
        }

        db.Customers.AddRange(
            new Customer { FirstName = "Aung", LastName = "Min", Email = "aung.min@example.com" },
            new Customer { FirstName = "Siti", LastName = "Rahman", Email = "siti.rahman@example.com" },
            new Customer { FirstName = "Wei", LastName = "Tan", Email = "wei.tan@example.com" });

        db.Products.AddRange(
            new Product
            {
                Name = "City Tour",
                Description = "A half-day walking tour of Singapore.",
                Price = 48.00m,
                Currency = "SGD",
                PaymentType = PaymentType.OneTime
            },
            new Product
            {
                Name = "Night Safari Ticket",
                Description = "One adult ticket for a night visit.",
                Price = 55.00m,
                Currency = "SGD",
                PaymentType = PaymentType.OneTime
            },
            new Product
            {
                Name = "Monthly Gym Pass",
                Description = "A monthly pass. Recurring here only means the card is saved for later.",
                Price = 89.00m,
                Currency = "SGD",
                PaymentType = PaymentType.Recurring
            },
            new Product
            {
                Name = "Weekly Meal Plan",
                Description = "A weekly meal plan. The card is collected and stored as a token.",
                Price = 32.50m,
                Currency = "SGD",
                PaymentType = PaymentType.Recurring
            });

        await db.SaveChangesAsync(cancellationToken);
    }
}
