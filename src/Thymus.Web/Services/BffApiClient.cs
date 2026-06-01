using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;

namespace Thymus.Web.Services;

public sealed class BffApiClient : IDisposable
{
    private const string SessionCookieName = "thymus_session";

    private readonly HttpClient _http;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public BffApiClient(string baseUrl, IHttpContextAccessor httpContextAccessor)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute),
        };

        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(username, password)),
        };

        ForwardSessionCookieToBff(request);

        using var response = await _http.SendAsync(request);
        ReflectSessionCookieToBrowser(response);

        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<BoardDto>?> GetBoardsAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/boards");
        ForwardSessionCookieToBff(request);

        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<BoardDto>>();
    }

    public async Task<IReadOnlyList<ThreadSummaryDto>?> GetThreadsAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/threads");
        ForwardSessionCookieToBff(request);

        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<ThreadSummaryDto>>();
    }

    public async Task<bool> LogoutAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");

        ForwardSessionCookieToBff(request);

        using var response = await _http.SendAsync(request);
        ReflectSessionCookieToBrowser(response);

        // Ensure browser cookie is cleared locally even if BFF does not emit Set-Cookie on unauthorized.
        DeleteSessionCookieFromBrowser();

        return response.IsSuccessStatusCode;
    }

    private void ForwardSessionCookieToBff(HttpRequestMessage request)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null)
            return;

        if (context.Request.Cookies.TryGetValue(SessionCookieName, out var sessionId)
            && !string.IsNullOrWhiteSpace(sessionId))
        {
            request.Headers.TryAddWithoutValidation("Cookie", $"{SessionCookieName}={sessionId}");
        }
    }

    private void ReflectSessionCookieToBrowser(HttpResponseMessage response)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null)
            return;

        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
            return;

        foreach (var header in setCookieHeaders)
        {
            if (!TryExtractCookieValue(header, SessionCookieName, out var sessionId))
                continue;

            context.Response.Cookies.Append(SessionCookieName, sessionId, new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                Expires = DateTimeOffset.UtcNow.AddHours(8),
            });

            return;
        }
    }

    private void DeleteSessionCookieFromBrowser()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null)
            return;

        context.Response.Cookies.Delete(SessionCookieName);
    }

    private static bool TryExtractCookieValue(string setCookieHeader, string cookieName, out string value)
    {
        value = string.Empty;

        var firstSegment = setCookieHeader.Split(';', 2)[0];
        var separatorIndex = firstSegment.IndexOf('=');
        if (separatorIndex <= 0)
            return false;

        var key = firstSegment[..separatorIndex].Trim();
        if (!string.Equals(key, cookieName, StringComparison.OrdinalIgnoreCase))
            return false;

        value = firstSegment[(separatorIndex + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    private sealed record LoginRequest(string Username, string Password);
}
