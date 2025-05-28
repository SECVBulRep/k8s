using System.Net;
using Npgsql;

namespace HostnameApi;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddOpenApi();

        //string connectionString = builder.Configuration.GetConnectionString("Postgres")!;      

        var dbHost = builder.Configuration["ConnectionStrings:PostgresHost"];
        var dbPort = builder.Configuration["ConnectionStrings:PostgresPort"];
        var dbName = builder.Configuration["ConnectionStrings:PostgresDb"];
        var dbUser = Environment.GetEnvironmentVariable("POSTGRES_USER");
        var dbPassword = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

        string connectionString = $"Host={dbHost};Port={dbPort};Username={dbUser};Password={dbPassword};Database={dbName}";

        var app = builder.Build();

        app.MapGet("/db-test", async () =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand("SELECT * FROM \"Shop\".\"Product\" ORDER BY \"Id\" ASC", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            var products = new List<Dictionary<string, object>>();

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.GetValue(i);
                }
                products.Add(row);
            }

            return Results.Ok(products);
        });


        app.MapGet("/ver", () =>
        {             
            return Results.Ok(10);
        });

        // Configure the HTTP request pipeline.
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
        })
        .WithName("GetWeatherForecast");

        app.Run();
    }
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

record WeatherForecastResponse(string Hostname, WeatherForecast[] Forecast);
