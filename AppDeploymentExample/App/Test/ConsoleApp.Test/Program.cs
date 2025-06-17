using StackExchange.Redis;
using System;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            // Конфигурация Sentinel
            var options = new ConfigurationOptions
            {
                EndPoints = { "172.16.29.111:26379" },
                Password = "your-secure-password",
                ServiceName = "mymaster",
                AllowAdmin = true,
                ConnectTimeout = 5000,
                SyncTimeout = 5000,
                AbortOnConnectFail = false
            };

            // Подключение
            using var redis = await ConnectionMultiplexer.ConnectAsync(options);
            Console.WriteLine("Connected to Redis Sentinel");

            // Получение мастера
            var server = redis.GetServer(redis.GetEndPoints()[0]);
            var masterInfo = await server.ExecuteAsync("SENTINEL", "get-master-addr-by-name", "mymaster");
            var masterAddr = $"{masterInfo[0]}:{masterInfo[1]}";
            Console.WriteLine($"Master: {masterAddr}");

            // Тест SET/GET
            var db = redis.GetDatabase();
            string key = "test-key";
            string value = "test-value";
            await db.StringSetAsync(key, value);
            Console.WriteLine($"SET {key} = {value}");

            var result = await db.StringGetAsync(key);
            Console.WriteLine($"GET {key} = {result}");

            // Проверка записи на реплику (должна завершиться ошибкой)
            try
            {
                var endpoints = redis.GetEndPoints();
                foreach (var endpoint in endpoints)
                {
                    var srv = redis.GetServer(endpoint);
                    var roleResult = await srv.ExecuteAsync("ROLE");
                    if (roleResult != null)
                    {
                        var roleArray = (RedisResult[])roleResult;
                        if (roleArray[0].ToString() == "slave")
                        {
                            await db.StringSetAsync("replica-test", "fail", when: When.Always, flags: CommandFlags.DemandReplica);
                            Console.WriteLine("Unexpected: Write to replica succeeded");
                        }
                    }
                }
            }
            catch (RedisServerException ex)
            {
                Console.WriteLine($"Expected error writing to replica: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}