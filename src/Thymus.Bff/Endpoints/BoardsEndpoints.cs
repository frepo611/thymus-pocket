using Microsoft.AspNetCore.Http.HttpResults;
using Thymus.Bff.Contracts;
using Thymus.SmfAdapter;

namespace Thymus.Bff.Endpoints;

public static class BoardsEndpoints
{
    public static void MapBoardsEndpoints(this WebApplication app, BffContext context)
    {
        app.MapGet("/api/boards", GetBoards(context)).RequireRateLimiting("read");
    }

    private static Delegate GetBoards(BffContext context) =>
        (HttpContext httpContext) => HandleGetBoards(httpContext, context);

    private static async Task<Results<Ok<IReadOnlyList<BoardDto>>, UnauthorizedHttpResult>> HandleGetBoards(
        HttpContext httpContext,
        BffContext context)
    {
        var session = SessionHelpers.TryGetSession(httpContext, context.Sessions, context.SessionCookieName, context.SessionStoreDirectory);
        if (session is null)
            return TypedResults.Unauthorized();

        using var client = new SmfHttpClient(context.SmfBaseUrl);
        if (!client.TryLoadCookies(session.CookieFilePath))
        {
            SessionHelpers.RemoveSession(httpContext, context.Sessions, context.SessionCookieName, context.SessionStoreDirectory);
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
