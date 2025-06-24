using System.Net.Http;
using System.Net.Http.Json;
using MassTransit;
using SeedWork.Requests;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient(); // добавим HttpClient
// Конфигурация (включая appsettings.json)
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

// Конфигурация MassTransit для RabbitMQ
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        
        var host = builder.Configuration["RabbitMQ:Host"];
        var port =  ushort.Parse(builder.Configuration["RabbitMQ:Port"]);
        var virtualHost = builder.Configuration["RabbitMQ:VirtualHost"];
        var username = builder.Configuration["RabbitMQ:Username"];
        var password = builder.Configuration["RabbitMQ:Password"];
                
        // Подключение к RabbitMQ кластеру
        cfg.Host(host, port, virtualHost, h =>
        {
            h.Username(username);
            h.Password(password);
        });

        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

app.MapGet("/proxy-weather",
    async (IHttpClientFactory httpClientFactory, IPublishEndpoint publishEndpoint, HttpContext context, IConfiguration _configuration) =>
    {
        try
        {
            await publishEndpoint.Publish(new WeatherRequested
            {
                UserAgent = context.Request.Headers.UserAgent.ToString(),
                RequestSource = "proxy-api",
                RemoteIpAddress = context.Connection.RemoteIpAddress?.ToString()
            });

            var client = httpClientFactory.CreateClient();

            var url = _configuration["Urls:WeatherApi"];
            
            var response =
                await client.GetFromJsonAsync<Dictionary<string, object>>($"{url}/weatherforecast");

            if (response == null)
            {
                return Results.Problem("❗ Ответ от api-weather пустой (null)");
            }

            response["ProxyTimeFIELD"] = DateTime.UtcNow;
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.Problem("❌ Ошибка: " + ex.Message);
        }
    });

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Service = "ProxyApi", Time = DateTime.UtcNow }));

app.Run();