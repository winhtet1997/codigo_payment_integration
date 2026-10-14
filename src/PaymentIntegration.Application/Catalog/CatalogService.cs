using Microsoft.Extensions.Logging;
using PaymentIntegration.Application.Abstractions;
using PaymentIntegration.Application.Contracts;

namespace PaymentIntegration.Application.Catalog;

/// <summary>
/// Read-heavy lists use cache-aside: look in Redis first, and only hit MySQL
/// on a miss. Transaction status is not cached. The result page must see the
/// row that the notification just wrote.
/// </summary>
public sealed class CatalogService
{
    public const string CustomersKey = "cache:customers";
    public const string ProductsKey = "cache:products";

    public static string CardsKey(int customerId) => $"cache:cards:{customerId}";

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly ICustomerRepository _customers;
    private readonly IProductRepository _products;
    private readonly ICustomerCardRepository _cards;
    private readonly IAppCache _cache;
    private readonly ILogger<CatalogService> _logger;

    public CatalogService(
        ICustomerRepository customers,
        IProductRepository products,
        ICustomerCardRepository cards,
        IAppCache cache,
        ILogger<CatalogService> logger)
    {
        _customers = customers;
        _products = products;
        _cards = cards;
        _cache = cache;
        _logger = logger;
    }

    public Task<IReadOnlyList<CustomerResponse>> GetCustomersAsync(CancellationToken cancellationToken) =>
        ReadThroughAsync(CustomersKey, ct => _customers.ListAsync(ct), cancellationToken);

    public Task<IReadOnlyList<ProductResponse>> GetProductsAsync(CancellationToken cancellationToken) =>
        ReadThroughAsync(ProductsKey, ct => _products.ListAsync(ct), cancellationToken);

    public Task<IReadOnlyList<SavedCardResponse>> GetCardsAsync(int customerId, CancellationToken cancellationToken) =>
        ReadThroughAsync(CardsKey(customerId), ct => _cards.ListByCustomerAsync(customerId, ct), cancellationToken);

    private async Task<IReadOnlyList<T>> ReadThroughAsync<T>(
        string key,
        Func<CancellationToken, Task<IReadOnlyList<T>>> load,
        CancellationToken cancellationToken)
    {
        try
        {
            var cached = await _cache.GetAsync<List<T>>(key, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }
        }
        catch (Exception exception)
        {
            // A cache miss because Redis is down should still show the dropdowns.
            // Checkout itself refuses to run without Redis, because the lock is required.
            _logger.LogWarning(exception, "Cache read failed for {CacheKey}. Reading from MySQL.", key);
        }

        var rows = await load(cancellationToken);

        try
        {
            await _cache.SetAsync(key, rows.ToList(), Ttl, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Cache write failed for {CacheKey}.", key);
        }

        return rows;
    }
}
