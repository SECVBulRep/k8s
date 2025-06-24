using System.Net;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using SeedWork.Redis;
using Serilog;
using Serilog.Events;

namespace HostnameApi;

public class Program
{
    public static void Main(string[] args)
    {
        // Настройка Serilog до Build
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        var builder = WebApplication.CreateBuilder(args);

        /// Подключение Serilog к ASP.NET Core
        builder.Host.UseSerilog();

        // Конфигурация (включая appsettings.json)
        builder.Configuration
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
            .AddEnvironmentVariables();

        builder.Services.AddOpenApi();
        builder.Services.AddSingleton<ProductService>();
        builder.Services.AddSingleton<IRedisService, RedisService>();

        var dbHost = builder.Configuration["ConnectionStrings:PostgresHost"];
        var dbPort = builder.Configuration["ConnectionStrings:PostgresPort"];
        var dbName = builder.Configuration["ConnectionStrings:PostgresDb"];
        
        var dbUser = Environment.GetEnvironmentVariable("POSTGRES_USER");
        var dbPassword = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

        if (builder.Environment.IsDevelopment())
        { 
             dbUser = builder.Configuration["ConnectionStrings:PostgresUser"];
             dbPassword = builder.Configuration["ConnectionStrings:PostgresPassword"];
        }


        var connectionString = $"Host={dbHost};Port={dbPort};Username={dbUser};Password={dbPassword};Database={dbName}";

        var app = builder.Build();

        Log.Information("Building PostgreSQL connection string:");
        Log.Information("PostgresHost: {host}", dbHost);
        Log.Information("PostgresPort: {port}", dbPort);
        Log.Information("PostgresDb: {db}", dbName);
        Log.Information("PostgresUser: {user}", dbUser);
        Log.Information("PostgresPassword: {pw}", dbPassword);
        Log.Information("Connection string: {conn}", connectionString);

        app.MapGet("/db-test", async ([FromServices] ProductService redisService) =>
        {
            try
            {
                var cacheKey = "products:all";
                var products = await redisService.GetCachedProductsAsync(cacheKey, async () =>
                {
                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();

                    await using var cmd = new NpgsqlCommand("SELECT * FROM \"Shop\".\"Product\" ORDER BY \"Id\" ASC", conn);
                    await using var reader = await cmd.ExecuteReaderAsync();

                    var productList = new List<Dictionary<string, object>>();

                    while (await reader.ReadAsync())
                    {
                        var row = new Dictionary<string, object>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[reader.GetName(i)] = reader.GetValue(i);
                        }
                        productList.Add(row);
                    }

                    return productList;
                });

                return Results.Ok(products);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while accessing PostgreSQL in /db-test");

                var user = Environment.GetEnvironmentVariable("POSTGRES_USER");
                var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

                Log.Information("PostgresUser: {user}", user);
                Log.Information("PostgresPassword: {pw}", password);

                return Results.Problem("Database connection failed: " + ex.Message);
            }
        });

        app.MapGet("/ver", () => Results.Ok(15));

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        var summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        app.MapGet("/weatherforecast", () =>
        {
            var hostname = Dns.GetHostName();
            var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
                .ToArray();
            return new WeatherForecastResponse(hostname, forecast);
        }).WithName("GetWeatherForecast");

        app.Run();
    }
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

record WeatherForecastResponse(string Hostname, WeatherForecast[] Forecast);
