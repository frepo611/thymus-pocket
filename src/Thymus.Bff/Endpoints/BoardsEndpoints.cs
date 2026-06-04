using Microsoft.AspNetCore.Http.HttpResults;
using Thymus.Bff.Contracts;
using Thymus.SmfAdapter;

namespace Thymus.Bff.Endpoints;

public static class BoardsEndpoints
{
    public static void MapBoardsEndpoints(this WebApplication app, BffContext bffContext)
    {
        app.MapGet("/api/boards", GetBoards(bffContext)).RequireRateLimiting("read");
    }

    private static Delegate GetBoards(BffContext bffContext) =>
        (HttpContext httpContext) => HandleGetBoards(httpContext, bffContext);

    private static async Task<Results<Ok<IReadOnlyList<BoardDto>>, UnauthorizedHttpResult>> HandleGetBoards(
        HttpContext httpContext,
        BffContext bffContext)
    {
        var session = SessionHelpers.TryGetSession(httpContext, bffContext.Sessions, bffContext.SessionCookieName, bffContext.SessionStoreDirectory);
        if (session is null)
            return TypedResults.Unauthorized();

        using var client = new SmfHttpClient(bffContext.SmfBaseUrl);
        if (!client.TryLoadCookies(session.CookieFilePath))
        {
            SessionHelpers.RemoveSession(httpContext, bffContext.Sessions, bffContext.SessionCookieName, bffContext.SessionStoreDirectory);
            return TypedResults.Unauthorized();
        }

        var boards = await client.GetBoardsAsync();
        var results = boards
            .Select(b => new
            {
                Id = SessionHelpers.GetBoardIdFromBoardUrl(b.Url),
                b.Name,
                b.Url,
                b.Category,
            })
            .Where(b => !string.IsNullOrWhiteSpace(b.Id))
            .Select(b => new BoardDto(b.Id!, b.Name, b.Url, b.Category))
            .ToList();

        SessionHelpers.TouchSession(session);
        return TypedResults.Ok<IReadOnlyList<BoardDto>>(results);
    }
}
