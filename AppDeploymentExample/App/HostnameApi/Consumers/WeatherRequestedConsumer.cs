// Consumers/WeatherRequestedConsumer.cs
using MassTransit;
using SeedWork.Redis;
using SeedWork.Requests;


namespace HostnameApi.Consumers;

public class WeatherRequestedConsumer : IConsumer<WeatherRequested>
{
    private readonly IRedisService _redis;
    private readonly ILogger<WeatherRequestedConsumer> _logger;

    public WeatherRequestedConsumer(IRedisService redis, ILogger<WeatherRequestedConsumer> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WeatherRequested> context)
    {
        var message = context.Message;
        
        _logger.LogInformation("Weather requested: {RequestId} from {Source} at {Time}", 
            message.RequestId, message.RequestSource, message.RequestTime);
            
        _logger.LogInformation("Request details: UserAgent={UserAgent}, IP={IP}", 
            message.UserAgent, message.RemoteIpAddress);

        try
        {
            // 📊 Увеличиваем счетчики в Redis
            var counterKey = "weather:requests:total";
            var todayKey = $"weather:requests:daily:{DateTime.UtcNow:yyyy-MM-dd}";
            var hourlyKey = $"weather:requests:hourly:{DateTime.UtcNow:yyyy-MM-dd-HH}";
            
            // Параллельно увеличиваем все счетчики
            var tasks = new[]
            {
                _redis.IncrementAsync(counterKey),
                _redis.IncrementAsync(todayKey),
                _redis.IncrementAsync(hourlyKey)
            };
            
            var results = await Task.WhenAll(tasks);
            
            _logger.LogInformation("✅ Statistics updated for {RequestId}: Total={Total}, Today={Today}, ThisHour={Hour}", 
                message.RequestId, results[0], results[1], results[2]);
                
            // Устанавливаем TTL для почасовых ключей (24 часа)
            await _redis.SetExpiryAsync(hourlyKey, TimeSpan.FromDays(1));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to update weather statistics for {RequestId}", message.RequestId);
            
            // Бросаем исключение для retry механизма MassTransit
            throw;
        }
    }
}