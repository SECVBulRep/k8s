using StackExchange.Redis;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Redis K8s Connection Test ===\n");

        var redisHost = "172.16.29.111:6379";
        
        try
        {
            // Конфигурация с явным указанием, что это не реплика
            var options = new ConfigurationOptions
            {
                EndPoints = { redisHost },
                AbortOnConnectFail = false,
                ConnectTimeout = 10000,
                SyncTimeout = 10000,
                AsyncTimeout = 10000,
                AllowAdmin = true,
                CommandMap = CommandMap.Create(new HashSet<string>
                {
                    // Убираем команды только для чтения, чтобы Redis понял что мы хотим писать
                }, available: false)
            };

            Console.WriteLine($"Connecting to Redis at {redisHost}...");
            
            using (var redis = await ConnectionMultiplexer.ConnectAsync(options))
            {
                Console.WriteLine("✅ Connected to Redis\n");
                
                var db = redis.GetDatabase();
                
                // Проверяем endpoints
                var endpoints = redis.GetEndPoints();
                foreach (var endpoint in endpoints)
                {
                    var server = redis.GetServer(endpoint);
                    Console.WriteLine($"Server: {endpoint}");
                    Console.WriteLine($"  Connected: {server.IsConnected}");
                    Console.WriteLine($"  Type: {server.ServerType}");
                    
                    if (server.IsConnected)
                    {
                        try
                        {
                            var info = await server.InfoAsync("replication");
                            var role = info[0].FirstOrDefault(x => x.Key == "role").Value;
                            Console.WriteLine($"  Role: {role}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  Role: Unable to determine ({ex.Message})");
                        }
                    }
                    Console.WriteLine();
                }

                // Ping test
                Console.WriteLine("Testing ping...");
                var pingTime = await db.PingAsync();
                Console.WriteLine($"✅ Ping response: {pingTime.TotalMilliseconds:F2}ms\n");

                // Write test с обработкой ошибок
                Console.WriteLine("Testing write operation...");
                var testKey = $"test:dotnet:{DateTime.Now.Ticks}";
                var testValue = $"Hello from .NET at {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                
                try
                {
                    // Попробуем записать с явным указанием флагов
                    await db.StringSetAsync(testKey, testValue, when: When.Always, flags: CommandFlags.DemandMaster);
                    Console.WriteLine($"✅ Written: {testKey} = {testValue}");
                    
                    // Читаем обратно
                    var readValue = await db.StringGetAsync(testKey);
                    Console.WriteLine($"✅ Read back: {readValue}");
                    
                    // Удаляем
                    await db.KeyDeleteAsync(testKey);
                    Console.WriteLine("✅ Key deleted\n");
                }
                catch (RedisConnectionException connEx)
                {
                    Console.WriteLine($"❌ Connection error during write: {connEx.Message}");
                    Console.WriteLine("\nПопробуем альтернативный способ...\n");
                    
                    // Пробуем через прямое выполнение команды
                    try
                    {
                        var result = await db.ExecuteAsync("SET", testKey, testValue);
                        Console.WriteLine($"✅ Direct SET command: {result}");
                        
                        var getResult = await db.ExecuteAsync("GET", testKey);
                        Console.WriteLine($"✅ Direct GET result: {getResult}");
                        
                        await db.ExecuteAsync("DEL", testKey);
                        Console.WriteLine("✅ Direct DEL completed");
                    }
                    catch (Exception ex2)
                    {
                        Console.WriteLine($"❌ Direct command also failed: {ex2.Message}");
                    }
                }
                
                Console.WriteLine("\n✅ Test completed!");
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

            // Дополнительная диагностика
            Console.WriteLine("\n--- Диагностика ---");
            Console.WriteLine("1. Проверьте доступность Redis:");
            Console.WriteLine($"   ping {redisHost.Split(':')[0]}");
            Console.WriteLine($"   telnet {redisHost.Replace(":", " ")}");
            Console.WriteLine("\n2. Проверьте статус Redis в K8s:");
            Console.WriteLine("   kubectl get svc -n redis");
            Console.WriteLine("   kubectl get pods -n redis");
            Console.WriteLine("\n3. Проверьте логи Redis:");
            Console.WriteLine("   kubectl logs -n redis redis-node-0");
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}