using System.Net.Http;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient(); // добавим HttpClient
var app = builder.Build();

app.MapGet("/proxy-weather", async (IHttpClientFactory httpClientFactory) =>
{
    var client = httpClientFactory.CreateClient();

    // Обращаемся к внутреннему сервису в Kubernetes
    var response = await client.GetFromJsonAsync<Dictionary<string, object>>("http://api-weather/weatherforecast");
    response["ProxyTimeFIELD"] = DateTime.UtcNow;
    return Results.Ok(response);
});

app.Run("http://0.0.0.0:80");
