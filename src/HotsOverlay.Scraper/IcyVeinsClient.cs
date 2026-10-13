using System.Net.Http.Headers;

namespace HotsOverlay.Scraper;

/// <summary>Shared HTTP client for the (low-rate, personal-use, locally cached) Icy Veins scraper.</summary>
public static class IcyVeinsClient
{
    public static readonly HttpClient Http = Create();

    private static HttpClient Create()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://www.icy-veins.com/"),
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("HotsOverlay", "0.1"));
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("(personal-use local talent-build cache)"));
        return client;
    }
}
