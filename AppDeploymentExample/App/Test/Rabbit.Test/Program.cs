using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

class Program
{
    private const string RABBITMQ_HOST = "172.16.29.118";
    private const int RABBITMQ_PORT = 5672;
    private const string RABBITMQ_USERNAME = "admin";
    private const string RABBITMQ_PASSWORD = "admin";
    private const string TEST_QUEUE = "test-cluster-ha-queue";
    private const string TEST_EXCHANGE = "test-cluster-ha-exchange";

    static async Task Main(string[] args)
    {
        Console.WriteLine("🐰 RabbitMQ HA Test with Quorum Queues");
        Console.WriteLine("=====================================");
        Console.WriteLine($"🔗 Connecting to: {RABBITMQ_HOST}:{RABBITMQ_PORT}");

        var factory = new ConnectionFactory()
        {
            HostName = RABBITMQ_HOST,
            Port = RABBITMQ_PORT,
            UserName = RABBITMQ_USERNAME,
            Password = RABBITMQ_PASSWORD,
            VirtualHost = "/",
            
            // HA settings для автоматического восстановления
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
            RequestedHeartbeat = TimeSpan.FromSeconds(60),
            TopologyRecoveryEnabled = true
        };

        try
        {
            using var connection = factory.CreateConnection("RabbitMQ-HA-Test");
            using var channel = connection.CreateModel();

            Console.WriteLine("✅ Connected to RabbitMQ cluster!");
            Console.WriteLine($"📡 Endpoint: {connection.Endpoint}");

            // Настройка топологии с HA
            await SetupHATopology(channel);

            // Тестируем отправку сообщений
            await SendTestMessages(channel);

            // Настраиваем consumer
            await ConsumeMessages(channel);

            // Циклическая отправка для тестирования failover
            await StartContinuousTesting(channel);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            Console.WriteLine($"📋 Details: {ex}");
            Environment.Exit(1);
        }
    }

    static async Task SetupHATopology(IModel channel)
    {
        Console.WriteLine("🔧 Setting up HA topology...");

        try
        {
            // Создаем exchange
            channel.ExchangeDeclare(
                exchange: TEST_EXCHANGE,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false
            );
            Console.WriteLine($"✅ Exchange '{TEST_EXCHANGE}' declared");

            // Создаем QUORUM QUEUE для HA
            var queueArgs = new Dictionary<string, object>
            {
                {"x-queue-type", "quorum"}  // КРИТИЧНО для HA!
            };

            channel.QueueDeclare(
                queue: TEST_QUEUE,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: queueArgs
            );
            Console.WriteLine($"✅ Quorum Queue '{TEST_QUEUE}' declared (HA enabled!)");

            // Связываем queue с exchange
            channel.QueueBind(
                queue: TEST_QUEUE,
                exchange: TEST_EXCHANGE,
                routingKey: "test"
            );
            Console.WriteLine("✅ Binding created");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Topology setup failed: {ex.Message}");
            throw;
        }

        await Task.Delay(100);
    }

    static async Task SendTestMessages(IModel channel)
    {
        Console.WriteLine("📤 Sending initial test messages...");

        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;  // Persistent messages для HA
        properties.MessageId = Guid.NewGuid().ToString();

        for (int i = 1; i <= 5; i++)
        {
            try
            {
                string message = $"HA Test message #{i} - {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                var body = Encoding.UTF8.GetBytes(message);

                properties.MessageId = Guid.NewGuid().ToString();
                properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                channel.BasicPublish(
                    exchange: TEST_EXCHANGE,
                    routingKey: "test",
                    basicProperties: properties,
                    body: body
                );

                Console.WriteLine($"  📩 Sent: {message}");
                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Send failed: {ex.Message}");
            }
        }
    }

    static async Task ConsumeMessages(IModel channel)
    {
        Console.WriteLine("📥 Setting up message consumer...");

        var consumer = new EventingBasicConsumer(channel);
        int messageCount = 0;

        consumer.Received += (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                messageCount++;

                Console.WriteLine($"  📨 Received #{messageCount}: {message}");

                // Manual ACK для надежности
                channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Consume error: {ex.Message}");
                // NACK при ошибке
                channel.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        // Настройки consumer для HA
        channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);
        
        var consumerTag = channel.BasicConsume(
            queue: TEST_QUEUE,
            autoAck: false,  // Manual ACK для надежности
            consumer: consumer
        );

        Console.WriteLine($"✅ Consumer started (tag: {consumerTag})");
        await Task.Delay(1000);
    }

    static async Task StartContinuousTesting(IModel channel)
    {
        Console.WriteLine("");
        Console.WriteLine("🔄 Starting continuous HA testing...");
        Console.WriteLine("💥 Try stopping k8s02 node during this test!");
        Console.WriteLine("🔄 Press Ctrl+C to exit...");
        Console.WriteLine("");

        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        
        int messageNumber = 6;  // Продолжаем нумерацию
        
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    string message = $"Continuous HA test #{messageNumber} - {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                    var body = Encoding.UTF8.GetBytes(message);

                    properties.MessageId = Guid.NewGuid().ToString();
                    properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                    channel.BasicPublish(
                        exchange: TEST_EXCHANGE,
                        routingKey: "test",
                        basicProperties: properties,
                        body: body
                    );

                    Console.WriteLine($"  🔁 Sent #{messageNumber}: {message}");
                    messageNumber++;

                    await Task.Delay(3000, cts.Token);  // Отправляем каждые 3 секунды
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Send error: {ex.Message}");
                    Console.WriteLine("⏳ Waiting for recovery...");
                    await Task.Delay(5000, cts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Нормальное завершение
        }

        Console.WriteLine("");
        Console.WriteLine("🏁 Continuous testing stopped");
        Console.WriteLine("📊 Final queue status:");
        
        try
        {
            // Показываем финальную статистику
            var queueInfo = channel.QueueDeclarePassive(TEST_QUEUE);
            Console.WriteLine($"  📋 Messages in queue: {queueInfo.MessageCount}");
            Console.WriteLine($"  👥 Active consumers: {queueInfo.ConsumerCount}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Cannot get queue stats: {ex.Message}");
        }

        Console.WriteLine("");
        Console.WriteLine("🎉 HA Test completed!");
        Console.WriteLine("");
        Console.WriteLine("🎯 Expected behavior:");
        Console.WriteLine("  ✅ Messages should continue flowing during node failure");
        Console.WriteLine("  ✅ Quorum queue survives node restarts");  
        Console.WriteLine("  ✅ Automatic reconnection to healthy nodes");
    }
}