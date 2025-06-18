using StackExchange.Redis;
using System;
using System.Threading.Tasks;

namespace RedisTestApp
{
    class Program
    {
        private static ConnectionMultiplexer? redis;
        private static IDatabase? db;

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Redis .NET Test Application ===\n");

            try
            {
                // Подключение к Redis через HAProxy
                await ConnectToRedis();
                
                // Тестирование базовых операций
                await TestBasicOperations();
                
                // Информация о сервере
                //await ShowServerInfo();
                
                Console.WriteLine("\n✅ Все операции выполнены успешно!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка: {ex.Message}");
            }
            finally
            {
                redis?.Dispose();
                Console.WriteLine("\nНажмите любую клавишу для выхода...");
                Console.ReadKey();
            }
        }

        static async Task ConnectToRedis()
        {
            Console.WriteLine("🔗 Подключение к Redis...");
            
            var config = ConfigurationOptions.Parse("172.16.29.110:6379");
            config.ConnectTimeout = 5000;
            config.SyncTimeout = 5000;
            config.AbortOnConnectFail = false;

            redis = await ConnectionMultiplexer.ConnectAsync(config);
            db = redis.GetDatabase();
            
            Console.WriteLine($"✅ Подключен к Redis: {redis.GetEndPoints()[0]}");
        }

        static async Task TestBasicOperations()
        {
            Console.WriteLine("\n📝 Тестирование базовых операций:");

            // SET операция
            string key = "test:app:timestamp";
            string value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            
            await db!.StringSetAsync(key, value);
            Console.WriteLine($"SET {key} = \"{value}\"");

            // GET операция
            var retrievedValue = await db.StringGetAsync(key);
            Console.WriteLine($"GET {key} = \"{retrievedValue}\"");

            // Множественная запись
            var batch = new KeyValuePair<RedisKey, RedisValue>[]
            {
                new("app:counter", 42),
                new("app:name", "Redis .NET Client"),
                new("app:version", "1.0.0")
            };

            await db.StringSetAsync(batch);
            Console.WriteLine("✅ Записано несколько ключей");

            // Множественное чтение
            var keys = new RedisKey[] { "app:counter", "app:name", "app:version" };
            var values = await db.StringGetAsync(keys);
            
            for (int i = 0; i < keys.Length; i++)
            {
                Console.WriteLine($"GET {keys[i]} = \"{values[i]}\"");
            }

            // Инкремент
            var newCounter = await db.StringIncrementAsync("app:counter", 10);
            Console.WriteLine($"INCR app:counter +10 = {newCounter}");

            // TTL (время жизни)
            await db.StringSetAsync("temp:key", "expires soon", TimeSpan.FromSeconds(30));
            var ttl = await db.KeyTimeToLiveAsync("temp:key");
            Console.WriteLine($"SET temp:key с TTL = {ttl?.TotalSeconds:F0} секунд");
        }

        static async Task ShowServerInfo()
        {
            Console.WriteLine("\n📊 Информация о сервере:");

            var server = redis!.GetServer(redis.GetEndPoints()[0]);
            
            // Основная информация
            var info = await server.InfoAsync("replication");
            foreach (var group in info)
            {
                foreach (var item in group)
                {
                    if (item.Key == "role" || item.Key == "connected_slaves")
                    {
                        Console.WriteLine($"{item.Key}: {item.Value}");
                    }
                }
            }

            // Проверка производительности
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await db!.StringGetAsync("test:latency");
            stopwatch.Stop();
            
            Console.WriteLine($"Latency: {stopwatch.ElapsedMilliseconds} ms");
            
            // Количество ключей
            var keyCount = await server.DatabaseSizeAsync();
            Console.WriteLine($"Всего ключей в БД: {keyCount}");
        }
    }
}