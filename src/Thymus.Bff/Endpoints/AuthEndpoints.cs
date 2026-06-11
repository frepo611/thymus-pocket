using Microsoft.AspNetCore.Http.HttpResults;
using Thymus.Bff.Contracts;
using Thymus.SmfAdapter;


namespace Thymus.Bff.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app, BffContext bffContext)
    {
        app.MapPost("/api/auth/login", Login(bffContext)).RequireRateLimiting("login");
        app.MapPost("/api/auth/logout", Logout(bffContext)).RequireRateLimiting("read");
    }

    private static Delegate Login(BffContext bffContext) =>
        (LoginRequest request, HttpContext httpContext)
            => HandleLogin(bffContext, request, httpContext);

    private static Delegate Logout(BffContext bffContext) =>
        (HttpContext httpContext)
            => HandleLogout(bffContext, httpContext);

    private static async Task<Results<Ok, BadRequest<string>, UnauthorizedHttpResult>> HandleLogin(
        BffContext bffContext,
        LoginRequest request,
        HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return TypedResults.BadRequest("Username and password are required.");

        var sessionId = SessionHelpers.TryGetSessionIdFromCookie(httpContext, bffContext.SessionCookieName)
            ?? SessionHelpers.CreateSessionId();
        var cookieFilePath = SessionHelpers.GetSessionCookieFilePath(bffContext.SessionStoreDirectory, sessionId);
        var tempCookieFilePath = cookieFilePath + ".tmp";

        try
        {
            using var client = new SmfHttpClient(bffContext.SmfBaseUrl);
            await client.LoginAsync(request.Username, request.Password);
            client.SaveCookies(tempCookieFilePath);

            File.Move(tempCookieFilePath, cookieFilePath, overwrite: true);

            bffContext.Sessions[sessionId] = new SessionState(cookieFilePath, DateTimeOffset.UtcNow);

            httpContext.Response.Cookies.Append(
                bffContext.SessionCookieName,
                sessionId,
                SessionHelpers.BuildSessionCookieOptions(httpContext));

            return TypedResults.Ok();
        }
        catch
        {
            if (File.Exists(tempCookieFilePath))
                File.Delete(tempCookieFilePath);

            return TypedResults.Unauthorized();
        }
    }

    private static async Task<Results<Ok, UnauthorizedHttpResult>> HandleLogout(
        BffContext context,
        HttpContext httpContext)
    {
        var session = SessionHelpers.TryGetSession(
            httpContext,
            context.Sessions,
            context.SessionCookieName,
            context.SessionStoreDirectory);
        if (session is null)
            return TypedResults.Unauthorized();

        try
        {
            using var client = new SmfHttpClient(context.SmfBaseUrl);
            client.TryLoadCookies(session.CookieFilePath);
            await client.EnsureLoggedOutAsync();
        }
        catch
        {
        }

        SessionHelpers.RemoveSession(
            httpContext,
            context.Sessions,
            context.SessionCookieName,
            context.SessionStoreDirectory);
        return TypedResults.Ok();
    }
}