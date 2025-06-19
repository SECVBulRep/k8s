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
            Console.WriteLine("=== Redis .NET Test Application с авторизацией ===\n");

            try
            {
                // Подключение к Redis через HAProxy с авторизацией
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
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
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
            Console.WriteLine("🔗 Подключение к Redis с авторизацией...");
            
            // ВАРИАНТ 1: Через ConfigurationOptions (рекомендуемо для ACL)
            var config = new ConfigurationOptions
            {
                EndPoints = { "172.16.29.110:6379" },
                User = "admin-user",  // ACL пользователь
                Password = GetRedisPassword(), // Пароль admin пользователя
                ConnectTimeout = 5000,
                SyncTimeout = 5000,
                AbortOnConnectFail = false,
                ConnectRetry = 3
            };

            // ВАРИАНТ 2: Через строку подключения (альтернатива)
            // var config = ConfigurationOptions.Parse($"172.16.29.110:6379,user=admin,password={GetRedisPassword()}");

            Console.WriteLine($"Подключаемся как пользователь: {config.User}");
            
            redis = await ConnectionMultiplexer.ConnectAsync(config);
            db = redis.GetDatabase();
            
            Console.WriteLine($"✅ Подключен к Redis: {redis.GetEndPoints()[0]}");
            Console.WriteLine($"✅ Авторизован как: {config.User}");
        }

        static string GetRedisPassword()
        {
            return "admin-secure-password";
        }

        static async Task TestBasicOperations()
        {
            Console.WriteLine("\n📝 Тестирование базовых операций:");

            // SET операция
            string key = "test:dotnet:timestamp";
            string value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            
            await db!.StringSetAsync(key, value);
            Console.WriteLine($"SET {key} = \"{value}\"");

            // GET операция
            var retrievedValue = await db.StringGetAsync(key);
            Console.WriteLine($"GET {key} = \"{retrievedValue}\"");

            // Множественная запись
            var batch = new KeyValuePair<RedisKey, RedisValue>[]
            {
                new("dotnet:counter", 42),
                new("dotnet:name", "Redis .NET Client with Auth"),
                new("dotnet:version", "2.0.0"),
                new("dotnet:user", "admin")
            };

            await db.StringSetAsync(batch);
            Console.WriteLine("✅ Записано несколько ключей");

            // Множественное чтение
            var keys = new RedisKey[] { "dotnet:counter", "dotnet:name", "dotnet:version", "dotnet:user" };
            var values = await db.StringGetAsync(keys);
            
            for (int i = 0; i < keys.Length; i++)
            {
                Console.WriteLine($"GET {keys[i]} = \"{values[i]}\"");
            }

            // Инкремент
            var newCounter = await db.StringIncrementAsync("dotnet:counter", 10);
            Console.WriteLine($"INCR dotnet:counter +10 = {newCounter}");

            // Работа со списками
            await db.ListLeftPushAsync("dotnet:logs", $"Login at {DateTime.Now}");
            await db.ListLeftPushAsync("dotnet:logs", $"Operation at {DateTime.Now}");
            var logCount = await db.ListLengthAsync("dotnet:logs");
            Console.WriteLine($"LIST dotnet:logs длина = {logCount}");

            var latestLog = await db.ListRightPopAsync("dotnet:logs");
            Console.WriteLine($"Последний лог: {latestLog}");

            // Работа с хешами
            await db.HashSetAsync("dotnet:config", new HashEntry[]
            {
                new("timeout", 30),
                new("retries", 3),
                new("environment", "production")
            });

            var timeout = await db.HashGetAsync("dotnet:config", "timeout");
            Console.WriteLine($"HASH dotnet:config timeout = {timeout}");

            // TTL (время жизни)
            await db.StringSetAsync("dotnet:temp", "expires soon", TimeSpan.FromSeconds(30));
            var ttl = await db.KeyTimeToLiveAsync("dotnet:temp");
            Console.WriteLine($"SET dotnet:temp с TTL = {ttl?.TotalSeconds:F0} секунд");
        }

        static async Task ShowServerInfo()
        {
            Console.WriteLine("\n📊 Информация о сервере:");

            try
            {
                var server = redis!.GetServer(redis.GetEndPoints()[0]);
                
                // Основная информация о репликации
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

                // Информация о подключении
                Console.WriteLine($"Endpoint: {redis.GetEndPoints()[0]}");
                Console.WriteLine($"Статус: {redis.GetStatus()}");
                Console.WriteLine($"Multiplexer ID: {redis.ClientName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Не удалось получить информацию о сервере: {ex.Message}");
                Console.WriteLine("(Это может быть связано с правами пользователя)");
            }
        }
    }
}