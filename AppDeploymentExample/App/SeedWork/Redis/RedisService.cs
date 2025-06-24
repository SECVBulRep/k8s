using System.Text.Json;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace SeedWork.Redis;

public class RedisService : IRedisService, IDisposable
{
    private readonly Lazy<Task<ConnectionMultiplexer>> _lazyConnection;
    private readonly IConfiguration _configuration;
    private readonly string _keyPrefix;

    public RedisService(IConfiguration configuration)
    {
        _configuration = configuration;
        _keyPrefix = _configuration["Redis:KeyPrefix"] ;
        _lazyConnection = new Lazy<Task<ConnectionMultiplexer>>(ConnectAsync);
    }


    private async Task<ConnectionMultiplexer> ConnectAsync()
    {
        var config = new ConfigurationOptions
        {
            EndPoints = { _configuration["Redis:ClusterNodes"] },
            User = _configuration["Redis:User"],
            Password = _configuration["Redis:Password"],
            ConnectTimeout = 5000,
            SyncTimeout = 5000,
            AbortOnConnectFail = false,
            ConnectRetry = 3
        };

        return await ConnectionMultiplexer.ConnectAsync(config);
    }

    private string GetPrefixedKey(string key)
    {
        if (string.IsNullOrEmpty(_keyPrefix))
            return key;
        
        return $"{_keyPrefix}:{key}";
    }

    public async Task<IDatabase> GetDatabaseAsync()
    {
        var connection = await _lazyConnection.Value;
        return connection.GetDatabase();
    }

    public IDatabase GetDatabase()
    {
        return _lazyConnection.Value.Result.GetDatabase();
    }

    public ConnectionMultiplexer GetConnection()
    {
        return _lazyConnection.Value.Result;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        var db = await GetDatabaseAsync();
        var prefixedKey = GetPrefixedKey(key); // Добавляем префикс
        var json = JsonSerializer.Serialize(value);
        await db.StringSetAsync(prefixedKey, json, expiry);
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var db = await GetDatabaseAsync();
        var prefixedKey = GetPrefixedKey(key); // Добавляем префикс
        var json = await db.StringGetAsync(prefixedKey);
        if (json.IsNullOrEmpty) return default;

        return JsonSerializer.Deserialize<T>(json!);
    }

    // Дополнительные методы для полноты функционала
    public async Task<bool> DeleteAsync(string key)
    {
        var db = await GetDatabaseAsync();
        var prefixedKey = GetPrefixedKey(key);
        return await db.KeyDeleteAsync(prefixedKey);
    }

    public async Task<bool> ExistsAsync(string key)
    {
        var db = await GetDatabaseAsync();
        var prefixedKey = GetPrefixedKey(key);
        return await db.KeyExistsAsync(prefixedKey);
    }

    public async Task<TimeSpan?> GetTtlAsync(string key)
    {
        var db = await GetDatabaseAsync();
        var prefixedKey = GetPrefixedKey(key);
        return await db.KeyTimeToLiveAsync(prefixedKey);
    }

    public async Task<bool> SetExpiryAsync(string key, TimeSpan expiry)
    {
        var db = await GetDatabaseAsync();
        var prefixedKey = GetPrefixedKey(key);
        return await db.KeyExpireAsync(prefixedKey, expiry);
    }

    public void Dispose()
    {
        if (_lazyConnection.IsValueCreated)
        {
            _lazyConnection.Value.Result?.Dispose();
        }
    }
}
