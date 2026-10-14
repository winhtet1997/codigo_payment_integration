using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentIntegration.Application.Abstractions;
using PaymentIntegration.Application.Checkout;
using PaymentIntegration.Infrastructure.Payments;
using PaymentIntegration.Infrastructure.Persistence;
using PaymentIntegration.Infrastructure.Redis;

namespace PaymentIntegration.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is missing.");

        var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(connectionString, serverVersion));

        services.Configure<TwoC2POptions>(configuration.GetSection(TwoC2POptions.SectionName));
        services.Configure<AppOptions>(configuration.GetSection(AppOptions.SectionName));

        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICustomerCardRepository, CustomerCardRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<DatabaseInitializer>();

        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.");
        services.AddSingleton(new RedisConnector(redisConnectionString));
        services.AddSingleton<IAppCache, RedisAppCache>();
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();

        services.AddHttpClient<IPaymentGateway, TwoC2PGateway>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddSingleton<ISignedPayloadReader, TwoC2PSignatureReader>();

        return services;
    }
}

public sealed class TwoC2PSignatureReader : ISignedPayloadReader
{
    private readonly TwoC2POptions _options;

    public TwoC2PSignatureReader(IOptions<TwoC2POptions> options)
    {
        _options = options.Value;
    }

    public PaymentResultNotice ReadNotification(string jwt)
    {
        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            throw new InvalidPaymentSignatureException("The merchant secret key is not configured.");
        }

        return new JwtCodec(_options.SecretKey).Read<ProviderResultWire>(jwt).ToNotice();
    }
}

/// <summary>
/// Lets `dotnet ef` build a context without starting the web app.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost;Port=3306;Database=payment;User=root;Password=root;";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)))
            .Options;

        return new AppDbContext(options);
    }
}
