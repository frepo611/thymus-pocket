using Thymus.SmfAdapter;

namespace Thymus.Bff.Endpoints;

public static class DebugEndpoints
{
    public static void MapDebugEndpoints(this WebApplication app, BffContext context)
    {
        if (!app.Environment.IsDevelopment())
            return;

        app.MapGet("/api/debug/html-structure", GetHtmlStructure(context));
    }

    private static Delegate GetHtmlStructure(BffContext context) =>
        (string url, HttpContext httpContext) => HandleGetHtmlStructure(url, httpContext, context);

    private static async Task<IResult> HandleGetHtmlStructure(
        string url,
        HttpContext httpContext,
        BffContext context)
    {
        var session = SessionHelpers.TryGetSession(httpContext, context.Sessions, context.SessionCookieName, context.SessionStoreDirectory);
        if (session is null)
            return Results.Unauthorized();

        using var client = new SmfHttpClient(context.SmfBaseUrl);
        if (!client.TryLoadCookies(session.CookieFilePath))
            return Results.Unauthorized();

        var html = await client.GetRawHtmlAsync(url);
        var info = SmfHtmlParser.DiagnoseHtml(html);
        return Results.Ok(info);
    }
}
