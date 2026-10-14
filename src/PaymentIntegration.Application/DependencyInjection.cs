using Microsoft.Extensions.DependencyInjection;
using PaymentIntegration.Application.Catalog;
using PaymentIntegration.Application.Checkout;
using PaymentIntegration.Application.Payments;

namespace PaymentIntegration.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CatalogService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<PaymentUpdateService>();
        services.AddScoped<TransactionQueryService>();
        return services;
    }
}
