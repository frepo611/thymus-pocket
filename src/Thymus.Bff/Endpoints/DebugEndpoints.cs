using Thymus.SmfAdapter;

namespace Thymus.Bff.Endpoints;

public static class DebugEndpoints
{
    public static void MapDebugEndpoints(this WebApplication app, BffContext bffContext)
    {
        if (!app.Environment.IsDevelopment())
            return;

        app.MapGet("/api/debug/html-structure", GetHtmlStructure(bffContext));
    }

    private static Delegate GetHtmlStructure(BffContext bffContext) =>
        (string url, HttpContext httpContext) => HandleGetHtmlStructure(url, httpContext, bffContext);

    private static async Task<IResult> HandleGetHtmlStructure(
        string url,
        HttpContext httpContext,
        BffContext bffContext)
    {
        var session = SessionHelpers.TryGetSession(httpContext, bffContext.Sessions, bffContext.SessionCookieName, bffContext.SessionStoreDirectory);
        if (session is null)
            return Results.Unauthorized();

        using var client = new SmfHttpClient(bffContext.SmfBaseUrl);
        if (!client.TryLoadCookies(session.CookieFilePath))
            return Results.Unauthorized();

        var html = await client.GetRawHtmlAsync(url);
        var info = SmfHtmlParser.DiagnoseHtml(html);
        return Results.Ok(info);
    }
}
