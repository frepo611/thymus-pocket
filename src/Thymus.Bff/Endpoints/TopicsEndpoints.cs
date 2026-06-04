using Microsoft.AspNetCore.Http.HttpResults;
using Thymus.Bff.Contracts;
using Thymus.SmfAdapter;

namespace Thymus.Bff.Endpoints;

public static class TopicsEndpoints
{
    public static void MapTopicsEndpoints(this WebApplication app, BffContext context)
    {
        app.MapGet("/api/topics", GetTopics(context)).RequireRateLimiting("read");
    }

    private static Delegate GetTopics(BffContext context) =>
        (string boardId, int start, HttpContext httpContext) =>
            HandleGetTopics(boardId, start, httpContext, context);

    private static async Task<Results<Ok<TopicsPageDto>, BadRequest<string>, NotFound<string>, UnauthorizedHttpResult>>
        HandleGetTopics(
            string boardId,
            int start,
            HttpContext httpContext,
            BffContext context)
    {
        if (string.IsNullOrWhiteSpace(boardId))
            return TypedResults.BadRequest("boardId is required.");

        if (!boardId.All(char.IsDigit))
            return TypedResults.BadRequest("boardId must be numeric.");

        if (start < 0)
            return TypedResults.BadRequest("start must be >= 0.");

        var session = SessionHelpers.TryGetSession(httpContext, context.Sessions, context.SessionCookieName, context.SessionStoreDirectory);
        if (session is null)
            return TypedResults.Unauthorized();

        using var client = new SmfHttpClient(context.SmfBaseUrl);
        if (!client.TryLoadCookies(session.CookieFilePath))
        {
            SessionHelpers.RemoveSession(httpContext, context.Sessions, context.SessionCookieName, context.SessionStoreDirectory);
            return TypedResults.Unauthorized();
        }

        var boardUrl = $"index.php?board={Uri.EscapeDataString(boardId + ".0")}";
        var page = await client.GetBoardTopicsPageAsync(boardUrl, start);
        var items = page.Items
            .Select(topic => new ThreadSummaryDto(
                Id: SessionHelpers.BuildThreadId(topic.Url),
                Title: topic.Title,
                Board: topic.Board,
                Url: topic.Url,
                LastPostBy: topic.LastPostBy,
                LastPostAt: topic.LastPostAt))
            .ToList();

        SessionHelpers.TouchSession(session);
        return TypedResults.Ok(new TopicsPageDto(items, page.NextStart));
    }
}
