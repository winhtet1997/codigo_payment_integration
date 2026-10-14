using System.Text.Json;
using PaymentIntegration.Application.Abstractions;
using PaymentIntegration.Application.Common;
using StackExchange.Redis;

namespace PaymentIntegration.Infrastructure.Redis;

public sealed class RedisConnector
{
    private readonly string _connectionString;
    private readonly object _gate = new();
    private IConnectionMultiplexer? _connection;

    public RedisConnector(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IDatabase Database()
    {
        if (_connection is null)
        {
            lock (_gate)
            {
                _connection ??= ConnectionMultiplexer.Connect(_connectionString);
            }
        }

        return _connection!.GetDatabase();
    }
}

public sealed class RedisAppCache : IAppCache
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RedisConnector _redis;

    public RedisAppCache(RedisConnector redis)
    {
        _redis = redis;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        var value = await _redis.Database().StringGetAsync(key);
        if (value.IsNullOrEmpty)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(value.ToString(), Json);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, Json);
        return _redis.Database().StringSetAsync(key, json, ttl);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken) =>
        _redis.Database().KeyDeleteAsync(key);
}

public sealed class RedisDistributedLock : IDistributedLock
{
    // Deletes the lock only when the value is still ours. A lock that expired and was
    // taken by someone else must not be deleted by the old owner.
    private const string ReleaseScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end
        """;

    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(20);

    private readonly RedisConnector _redis;

    public RedisDistributedLock(RedisConnector redis)
    {
        _redis = redis;
    }

    public async Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var db = Database();
        var lockKey = "lock:" + key;
        var owner = Guid.NewGuid().ToString("N");
        var started = DateTime.UtcNow;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var acquired = await db.StringSetAsync(lockKey, owner, LockTtl, When.NotExists);
            if (acquired)
            {
                try
                {
                    return await action();
                }
                finally
                {
                    await db.ScriptEvaluateAsync(ReleaseScript, new RedisKey[] { lockKey }, new RedisValue[] { owner });
                }
            }

            if (DateTime.UtcNow - started >= MaxWait)
            {
                throw AppException.Conflict(
                    "Another checkout with this Idempotency-Key is still running. Wait for it to finish and submit again.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }
    }

    private IDatabase Database()
    {
        try
        {
            return _redis.Database();
        }
        catch (Exception exception)
        {
            throw AppException.Unavailable("Redis is required to lock a checkout. Start Redis and try again. " + exception.Message);
        }
    }
}
