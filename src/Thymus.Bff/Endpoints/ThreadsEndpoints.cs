using Microsoft.AspNetCore.Http.HttpResults;
using Thymus.Bff.Contracts;
using Thymus.SmfAdapter;

namespace Thymus.Bff.Endpoints;

public static class ThreadsEndpoints
{
    public static void MapThreadsEndpoints(this WebApplication app, BffContext bffContext)
    {
        app.MapGet("/api/threads", GetAll(bffContext)).RequireRateLimiting("read");
        app.MapGet("/api/threads/{id}", GetById(bffContext)).RequireRateLimiting("read");
        app.MapPost("/api/threads/{id}/replies", PostReply(bffContext)).RequireRateLimiting("write");
    }

    private static Delegate GetAll(BffContext bffContext) =>
        (HttpContext httpContext) => HandleGetAll(httpContext, bffContext);

    private static async Task<Results<Ok<IReadOnlyList<ThreadSummaryDto>>, UnauthorizedHttpResult>> HandleGetAll(
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

        var topics = await client.GetAllTopicsAsync();
        var results = topics
            .Select(topic => new ThreadSummaryDto(
                Id: SessionHelpers.BuildThreadId(topic.Url),
                Title: topic.Title,
                Board: topic.Board,
                Url: topic.Url,
                LastPostBy: topic.LastPostBy,
                LastPostAt: topic.LastPostAt))
            .ToList();

        SessionHelpers.TouchSession(session);
        return TypedResults.Ok<IReadOnlyList<ThreadSummaryDto>>(results);
    }

    private static Delegate GetById(BffContext bffContext) =>
        (string id, HttpContext httpContext) =>
            HandleGetById(id, httpContext, bffContext);

    private static async Task<Results<Ok<ThreadDetailsDto>, NotFound<string>, UnauthorizedHttpResult>> HandleGetById(
        string id,
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

        var topics = await client.GetAllTopicsAsync();
        var topic = topics.FirstOrDefault(t => string.Equals(SessionHelpers.BuildThreadId(t.Url), id, StringComparison.Ordinal));
        if (topic is null)
            return TypedResults.NotFound("Thread not found.");

        var posts = await client.GetTopicAsync(topic.Url);
        var dto = new ThreadDetailsDto(
            Id: id,
            Title: topic.Title,
            Url: topic.Url,
            Posts: posts.Select(p => new PostDto(p.MessageId, p.Author, p.Body, p.PostedAt)).ToList());

        SessionHelpers.TouchSession(session);
        return TypedResults.Ok(dto);
    }

    private static Delegate PostReply(BffContext bffContext) =>
        (string id, ReplyRequestDto request, HttpContext httpContext) =>
            HandlePostReply(id, request, httpContext, bffContext);

    private static async Task<Results<Ok, BadRequest<string>, NotFound<string>, UnauthorizedHttpResult>> HandlePostReply(
        string id,
        ReplyRequestDto request,
        HttpContext httpContext,
        BffContext bffContext)
    {
        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Message))
            return TypedResults.BadRequest("Subject and message are required.");

        var session = SessionHelpers.TryGetSession(httpContext, bffContext.Sessions, bffContext.SessionCookieName, bffContext.SessionStoreDirectory);
        if (session is null)
            return TypedResults.Unauthorized();

        using var client = new SmfHttpClient(bffContext.SmfBaseUrl);
        if (!client.TryLoadCookies(session.CookieFilePath))
        {
            SessionHelpers.RemoveSession(httpContext, bffContext.Sessions, bffContext.SessionCookieName, bffContext.SessionStoreDirectory);
            return TypedResults.Unauthorized();
        }

        var topics = await client.GetAllTopicsAsync();
        var topic = topics.FirstOrDefault(t => string.Equals(SessionHelpers.BuildThreadId(t.Url), id, StringComparison.Ordinal));
        if (topic is null)
            return TypedResults.NotFound("Thread not found.");

        await client.PostReplyAsync(topic.Url, request.Subject, request.Message);
        client.SaveCookies(session.CookieFilePath);

        SessionHelpers.TouchSession(session);
        return TypedResults.Ok();
    }
}
