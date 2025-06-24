namespace SeedWork.Requests;

public record WeatherRequested
{
    public string RequestId { get; init; } = Guid.NewGuid().ToString();
    public DateTime RequestTime { get; init; } = DateTime.UtcNow;
    public string? UserAgent { get; init; }
    public string? RequestSource { get; init; } = "proxy-api";
    public string? RemoteIpAddress { get; init; }
}