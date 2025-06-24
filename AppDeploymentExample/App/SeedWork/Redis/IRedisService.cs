using StackExchange.Redis;

namespace SeedWork.Redis;

public interface IRedisService
{
    IDatabase GetDatabase();
    Task<IDatabase> GetDatabaseAsync();
    ConnectionMultiplexer GetConnection();

    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
    Task<T?> GetAsync<T>(string key);
}