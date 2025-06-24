using Microsoft.Extensions.Configuration;
using SeedWork.Redis;
using StackExchange.Redis;
using Serilog;

namespace HostnameApi;

public class ProductService
{
    private readonly IRedisService _redisService;
    private readonly IConnectionMultiplexer _redis;
    private readonly string _password;

    public ProductService(IConfiguration configuration, IRedisService redisService)
    {
        _redisService = redisService;
    }


    public async Task<List<Dictionary<string, object>>> GetCachedProductsAsync(string cacheKey,
        Func<Task<List<Dictionary<string, object>>>> fetchFromDb)
    {
        var cached = await _redisService.GetAsync<List<Dictionary<string, object>>>(cacheKey);
        if (cached != null)
        {
            Log.Information("Cache hit for key: {key}", cacheKey);
            return cached;
        }
        Log.Information("Cache miss for key: {key}, fetching from DB", cacheKey);
        var products = await fetchFromDb();
        await _redisService.SetAsync(cacheKey, products, TimeSpan.FromMinutes(5));
        return products;
    }
}