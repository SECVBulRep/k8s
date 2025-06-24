using StackExchange.Redis;

namespace SeedWork.Redis;

public interface IRedisService
{
    IDatabase GetDatabase();
    Task<IDatabase> GetDatabaseAsync();
    ConnectionMultiplexer GetConnection();

    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
    Task<T?> GetAsync<T>(string key);
    
    // Новые методы для счетчиков
    Task<long> IncrementAsync(string key);
    Task<long> IncrementByAsync(string key, long value);
    Task<bool> SetExpiryAsync(string key, TimeSpan expiry);
}