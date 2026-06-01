using System.Net;
using System.Net.Http.Json;

namespace Thymus.Web.Services;

public sealed class BffApiClient : IDisposable
{
    private readonly HttpClient _http;

    public BffApiClient(string baseUrl)
    {
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute),
        };
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        using var response = await _http.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));
        return response.IsSuccessStatusCode;
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    private sealed record LoginRequest(string Username, string Password);
}
