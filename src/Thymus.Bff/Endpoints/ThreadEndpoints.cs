using Microsoft.AspNetCore.Http.HttpResults;
using Thymus.Bff.Contracts;
using Thymus.SmfAdapter;

namespace Thymus.Bff.Endpoints;

public static class ThreadEndpoints
{
    public static void MapThreadEndpoints(this WebApplication app, BffContext context)
    {
        app.MapGet("/api/thread", GetThread(context)).RequireRateLimiting("read");
        app.MapPost("/api/thread/reply", PostReply(context)).RequireRateLimiting("write");
    }

    private static Delegate GetThread(BffContext context) =>
        (string url, int start, HttpContext httpContext, bool newestFirst) =>
            HandleGetThread(url, start, httpContext, newestFirst, context);

    private static async Task<Results<Ok<PostsPageDto>, BadRequest<string>, UnauthorizedHttpResult>> HandleGetThread(
        string url,
        int start,
        HttpContext httpContext,
        bool newestFirst,
        BffContext context)
    {
        if (string.IsNullOrWhiteSpace(url))
            return TypedResults.BadRequest("url is required.");

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

        var page = await client.GetThreadPageAsync(url, start, newestFirst);
        var posts = page.Posts
            .Select(p => new PostDto(
                MessageId: p.MessageId,
                Author: p.Author,
                Body: p.Body,
                PostedAt: p.PostedAt))
            .ToList();

        SessionHelpers.TouchSession(session);
        return TypedResults.Ok(new PostsPageDto(page.Title, posts, page.NextStart));
    }

    private static Delegate PostReply(BffContext context) =>
        (ThreadReplyRequest request, HttpContext httpContext) =>
            HandlePostReply(request, httpContext, context);

    private static async Task<Results<Ok, BadRequest<string>, UnauthorizedHttpResult>> HandlePostReply(
        ThreadReplyRequest request,
        HttpContext httpContext,
        BffContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            return TypedResults.BadRequest("url is required.");

        if (string.IsNullOrWhiteSpace(request.Message))
            return TypedResults.BadRequest("message is required.");

        var session = SessionHelpers.TryGetSession(httpContext, context.Sessions, context.SessionCookieName, context.SessionStoreDirectory);
        if (session is null)
            return TypedResults.Unauthorized();

        using var client = new SmfHttpClient(context.SmfBaseUrl);
        if (!client.TryLoadCookies(session.CookieFilePath))
        {
            SessionHelpers.RemoveSession(httpContext, context.Sessions, context.SessionCookieName, context.SessionStoreDirectory);
            return TypedResults.Unauthorized();
        }

        var subject = string.IsNullOrWhiteSpace(request.Subject) ? string.Empty : request.Subject;
        await client.PostReplyAsync(request.Url, subject, request.Message);
        client.SaveCookies(session.CookieFilePath);

        SessionHelpers.TouchSession(session);
        return TypedResults.Ok();
    }
}
