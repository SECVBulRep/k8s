using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using Serilog;

namespace HostnameApi;

public class RedisService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _password;

    public RedisService(IConfiguration configuration)
    {
        var redisConfig = configuration.GetSection("Redis");
        var clusterNodes = redisConfig["ClusterNodes"];
        _password = redisConfig["Password"];

        if (string.IsNullOrEmpty(clusterNodes))
        {
            throw new ArgumentException("Redis ClusterNodes configuration is missing.");
        }

        var options = new ConfigurationOptions
        {
            EndPoints = { },
            Password = _password,
            AllowAdmin = true,
            AbortOnConnectFail = false,
            ConnectTimeout = 5000,
            SyncTimeout = 5000
        };

        foreach (var node in clusterNodes.Split(','))
        {
            options.EndPoints.Add(node);
        }

        try
        {
            _redis = ConnectionMultiplexer.Connect(options);
            Log.Information("Connected to Redis cluster: {nodes}", clusterNodes);
        }
        catch (RedisConnectionException ex)
        {
            Log.Error(ex, "Failed to connect to Redis cluster: {nodes}", clusterNodes);
            throw;
        }
    }

    public async Task<string> GetAsync(string key)
    {
        var db = _redis.GetDatabase();
        return await db.StringGetAsync(key);
    }

    public async Task SetAsync(string key, string value, TimeSpan? expiry = null)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(key, value, expiry);
    }

    public async Task<List<Dictionary<string, object>>> GetCachedProductsAsync(string cacheKey, Func<Task<List<Dictionary<string, object>>>> fetchFromDb)
    {
        var cached = await GetAsync(cacheKey);
        if (cached != null)
        {            
            Log.Information("Cache hit for key: {key}", cacheKey);
            return System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(cached);
        }

        Log.Information("Cache miss for key: {key}, fetching from DB", cacheKey);
        var products = await fetchFromDb();
        await SetAsync(cacheKey, System.Text.Json.JsonSerializer.Serialize(products), TimeSpan.FromMinutes(5));
        return products;
    }
}