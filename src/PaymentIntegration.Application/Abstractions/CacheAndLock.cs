namespace PaymentIntegration.Application.Abstractions;

public interface IAppCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken);

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken);

    Task RemoveAsync(string key, CancellationToken cancellationToken);
}

public interface IDistributedLock
{
    /// <summary>
    /// Runs <paramref name="action"/> while this process owns the Redis lock.
    /// Other callers with the same key wait. If the lock is still busy when
    /// the wait expires, this throws HTTP 409.
    /// </summary>
    Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action, CancellationToken cancellationToken);
}
