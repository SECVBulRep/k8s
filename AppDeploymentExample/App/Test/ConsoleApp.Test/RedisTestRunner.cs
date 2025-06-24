using RedisTestApp;
using SeedWork.Redis;

namespace ConsoleApp.Test;

public class RedisTestRunner
{
    private readonly IRedisService _redis;

    public RedisTestRunner(IRedisService redis)
    {
        _redis = redis;
    }

    public async Task RunAsync()
    {

        while (true)
        {
            await Task.Delay(500);
            try
            {
                var profile = new UserProfile("bulat", 32, DateTime.Now);
                await _redis.SetAsync("user:profile", profile, TimeSpan.FromMinutes(5));

                var loaded = await _redis.GetAsync<UserProfile>("user:profile");
                Console.WriteLine($"Loaded user: {loaded?.Username}, age: {loaded?.Age} , stamp: {loaded?.Stamp}");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
       
    }
}