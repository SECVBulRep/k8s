using StackExchange.Redis;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Redis K8s Connection Test (через Sentinel) ===\n");

        var sentinelHost = "172.16.29.111:26379";  // Sentinel на порту 26379!
        var serviceName = "mymaster";
        
        try
        {
            // Шаг 1: Подключаемся к Sentinel чтобы найти master
            Console.WriteLine($"Connecting to Sentinel at {sentinelHost}...");
            
            var sentinelOptions = new ConfigurationOptions
            {
                EndPoints = { sentinelHost },
                CommandMap = CommandMap.Sentinel,
                AbortOnConnectFail = false,
                AllowAdmin = true,
                TieBreaker = ""
            };

            using (var sentinel = await ConnectionMultiplexer.ConnectAsync(sentinelOptions))
            {
                Console.WriteLine("✅ Connected to Sentinel\n");
                
                // Получаем информацию о master
                var sentinelServer = sentinel.GetServer(sentinel.GetEndPoints()[0]);
                var masterInfo = await sentinelServer.SentinelMasterAsync(serviceName);
                
                Console.WriteLine("Master information from Sentinel:");
                foreach (var item in masterInfo)
                {
                    if (item.Key == "ip" || item.Key == "port" || item.Key == "flags")
                    {
                        Console.WriteLine($"  {item.Key}: {item.Value}");
                    }
                }
                
                var masterIp = masterInfo.FirstOrDefault(x => x.Key == "ip").Value;
                var masterPort = masterInfo.FirstOrDefault(x => x.Key == "port").Value;
                var masterEndpoint = $"{masterIp}:{masterPort}";
                
                Console.WriteLine($"\n✅ Found master at: {masterEndpoint}\n");
                
                // Шаг 2: Подключаемся к найденному master
                var redisOptions = new ConfigurationOptions
                {
                    EndPoints = { masterEndpoint },
                    AbortOnConnectFail = false,
                    ConnectTimeout = 10000,
                    SyncTimeout = 10000,
                    AllowAdmin = true
                };

                Console.WriteLine($"Connecting to Redis master at {masterEndpoint}...");
                
                using (var redis = await ConnectionMultiplexer.ConnectAsync(redisOptions))
                {
                    Console.WriteLine("✅ Connected to Redis master\n");
                    
                    var db = redis.GetDatabase();
                    
                    // Проверяем что это действительно master
                    var server = redis.GetServer(redis.GetEndPoints()[0]);
                    var info = await server.InfoAsync("replication");
                    var role = info[0].FirstOrDefault(x => x.Key == "role").Value;
                    Console.WriteLine($"Server role: {role}");
                    
                    if (role != "master")
                    {
                        Console.WriteLine("⚠️  Warning: Connected to replica, not master!");
                    }
                    
                    // Ping test
                    Console.WriteLine("\nTesting ping...");
                    var pingTime = await db.PingAsync();
                    Console.WriteLine($"✅ Ping response: {pingTime.TotalMilliseconds:F2}ms\n");

                    // Write test
                    Console.WriteLine("Testing write operation...");
                    var testKey = $"test:dotnet:{DateTime.Now.Ticks}";
                    var testValue = $"Hello from .NET at {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                    
                    await db.StringSetAsync(testKey, testValue);
                    Console.WriteLine($"✅ Written: {testKey} = {testValue}");
                    
                    var readValue = await db.StringGetAsync(testKey);
                    Console.WriteLine($"✅ Read back: {readValue}");
                    
                    await db.KeyDeleteAsync(testKey);
                    Console.WriteLine("✅ Key deleted\n");
                    
                    Console.WriteLine("✅ All tests completed successfully!");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Error: {ex.Message}");
            Console.WriteLine($"   Type: {ex.GetType().Name}");
            
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner: {ex.InnerException.Message}");
            }
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}