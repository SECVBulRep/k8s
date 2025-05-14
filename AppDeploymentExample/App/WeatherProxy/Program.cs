using System.Net.Http;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient(); // добавим HttpClient
var app = builder.Build();

app.MapGet("/proxy-weather", async (IHttpClientFactory httpClientFactory) =>
{
    try
    {
        var client = httpClientFactory.CreateClient();
        var response = await client.GetFromJsonAsync<Dictionary<string, object>>("http://api-weather/weatherforecast");

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

app.Run("http://0.0.0.0:80");
