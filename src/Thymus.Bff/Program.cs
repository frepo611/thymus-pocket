using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Thymus.Bff.Contracts;
using Thymus.SmfAdapter;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var smfBaseUrl = builder.Configuration["Smf:BaseUrl"]
    ?? throw new InvalidOperationException("Smf:BaseUrl is required.");

var sessionStoreDirectoryRaw = builder.Configuration["Session:StoreDirectory"] ?? ".sessions";
var sessionCookieName = builder.Configuration["Session:CookieName"] ?? "thymus_session";
var sessionStoreDirectory = Path.IsPathRooted(sessionStoreDirectoryRaw)
    ? sessionStoreDirectoryRaw
    : Path.Combine(app.Environment.ContentRootPath, sessionStoreDirectoryRaw);
Directory.CreateDirectory(sessionStoreDirectory);

var sessions = new ConcurrentDictionary<string, SessionState>(StringComparer.Ordinal);

app.MapPost("/api/auth/login", async Task<Results<Ok, BadRequest<string>, UnauthorizedHttpResult>> (
    LoginRequest request,
    HttpContext httpContext) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        return TypedResults.BadRequest("Username and password are required.");

    var sessionId = TryGetSessionIdFromCookie(httpContext, sessionCookieName) ?? CreateSessionId();
    var cookieFilePath = GetSessionCookieFilePath(sessionStoreDirectory, sessionId);
    var tempCookieFilePath = cookieFilePath + ".tmp";

    try
    {
        using var client = new SmfHttpClient(smfBaseUrl);
        await client.LoginAsync(request.Username, request.Password);
        client.SaveCookies(tempCookieFilePath);

        File.Move(tempCookieFilePath, cookieFilePath, overwrite: true);

        sessions[sessionId] = new SessionState(cookieFilePath, DateTimeOffset.UtcNow);

        httpContext.Response.Cookies.Append(sessionCookieName, sessionId, BuildSessionCookieOptions(httpContext));
        return TypedResults.Ok();
    }
    catch
    {
        if (File.Exists(tempCookieFilePath))
            File.Delete(tempCookieFilePath);

        return TypedResults.Unauthorized();
    }
});

app.MapPost("/api/auth/logout", async Task<Results<Ok, UnauthorizedHttpResult>> (HttpContext httpContext) =>
{
    var session = TryGetSession(httpContext, sessions, sessionCookieName, sessionStoreDirectory);
    if (session is null)
        return TypedResults.Unauthorized();

    try
    {
        using var client = new SmfHttpClient(smfBaseUrl);
        client.TryLoadCookies(session.CookieFilePath);
        await client.EnsureLoggedOutAsync();
    }
    catch
    {
        // Best effort logout towards SMF; local session is still removed.
    }

    RemoveSession(httpContext, sessions, sessionCookieName, sessionStoreDirectory);
    return TypedResults.Ok();
});

app.MapGet("/api/boards", async Task<Results<Ok<IReadOnlyList<BoardDto>>, UnauthorizedHttpResult>> (HttpContext httpContext) =>
{
    var session = TryGetSession(httpContext, sessions, sessionCookieName, sessionStoreDirectory);
    if (session is null)
        return TypedResults.Unauthorized();

    using var client = new SmfHttpClient(smfBaseUrl);
    if (!client.TryLoadCookies(session.CookieFilePath))
    {
        RemoveSession(httpContext, sessions, sessionCookieName, sessionStoreDirectory);
        return TypedResults.Unauthorized();
    }

    var boards = await client.GetBoardsAsync();
    var results = boards
        .Select(b => new
        {
            Id = GetBoardIdFromBoardUrl(b.Url),
            b.Name,
            b.Url,
            b.Category,
        })
        .Where(b => !string.IsNullOrWhiteSpace(b.Id))
        .Select(b => new BoardDto(b.Id!, b.Name, b.Url, b.Category))
        .ToList();

    TouchSession(session);
    return TypedResults.Ok<IReadOnlyList<BoardDto>>(results);
});

app.MapGet("/api/topics", async Task<Results<Ok<TopicsPageDto>, BadRequest<string>, NotFound<string>, UnauthorizedHttpResult>> (
    string boardId,
    int start,
    HttpContext httpContext) =>
{
    if (string.IsNullOrWhiteSpace(boardId))
        return TypedResults.BadRequest("boardId is required.");

    if (!boardId.All(char.IsDigit))
        return TypedResults.BadRequest("boardId must be numeric.");

    if (start < 0)
        return TypedResults.BadRequest("start must be >= 0.");

    var session = TryGetSession(httpContext, sessions, sessionCookieName, sessionStoreDirectory);
    if (session is null)
        return TypedResults.Unauthorized();

    using var client = new SmfHttpClient(smfBaseUrl);
    if (!client.TryLoadCookies(session.CookieFilePath))
    {
        RemoveSession(httpContext, sessions, sessionCookieName, sessionStoreDirectory);
        return TypedResults.Unauthorized();
    }

    var boardUrl = $"index.php?board={Uri.EscapeDataString(boardId + ".0")}";
    var page = await client.GetBoardTopicsPageAsync(boardUrl, start);
    var items = page.Items
        .Select(topic => new ThreadSummaryDto(
            Id: BuildThreadId(topic.Url),
            Title: topic.Title,
            Board: topic.Board,
            Url: topic.Url,
            LastPostBy: topic.LastPostBy,
            LastPostAt: topic.LastPostAt))
        .ToList();

    TouchSession(session);
    return TypedResults.Ok(new TopicsPageDto(items, page.NextStart));
});

app.MapGet("/api/thread", async Task<Results<Ok<PostsPageDto>, BadRequest<string>, UnauthorizedHttpResult>> (
    string url,
    int start,
    HttpContext httpContext,
    bool newestFirst = true) =>
{
    if (string.IsNullOrWhiteSpace(url))
        return TypedResults.BadRequest("url is required.");

    if (start < 0)
        return TypedResults.BadRequest("start must be >= 0.");

    var session = TryGetSession(httpContext, sessions, sessionCookieName, sessionStoreDirectory);
    if (session is null)
        return TypedResults.Unauthorized();

    using var client = new SmfHttpClient(smfBaseUrl);
    if (!client.TryLoadCookies(session.CookieFilePath))
    {
        RemoveSession(httpContext, sessions, sessionCookieName, sessionStoreDirectory);
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

    TouchSession(session);
    return TypedResults.Ok(new PostsPageDto(page.Title, posts, page.NextStart));
});

app.MapPost("/api/thread/reply", async Task<Results<Ok, BadRequest<string>, UnauthorizedHttpResult>> (
    ThreadReplyRequest request,
    HttpContext httpContext) =>
{
    if (string.IsNullOrWhiteSpace(request.Url))
        return TypedResults.BadRequest("url is required.");

    if (string.IsNullOrWhiteSpace(request.Message))
        return TypedResults.BadRequest("message is required.");

    var session = TryGetSession(httpContext, sessions, sessionCookieName, sessionStoreDirectory);
    if (session is null)
        return TypedResults.Unauthorized();

    using var client = new SmfHttpClient(smfBaseUrl);
    if (!client.TryLoadCookies(session.CookieFilePath))
    {
        RemoveSession(httpContext, sessions, sessionCookieName, sessionStoreDirectory);
        return TypedResults.Unauthorized();
    }

    var subject = string.IsNullOrWhiteSpace(request.Subject) ? string.Empty : request.Subject;
    await client.PostReplyAsync(request.Url, subject, request.Message);
    client.SaveCookies(session.CookieFilePath);

    TouchSession(session);
    return TypedResults.Ok();
});

app.MapGet("/api/threads", async Task<Results<Ok<IReadOnlyList<ThreadSummaryDto>>, UnauthorizedHttpResult>> (HttpContext httpContext) =>
{
    var session = TryGetSession(httpContext, sessions, sessionCookieName, sessionStoreDirectory);
    if (session is null)
        return TypedResults.Unauthorized();

    using var client = new SmfHttpClient(smfBaseUrl);
    if (!client.TryLoadCookies(session.CookieFilePath))
    {
        RemoveSession(httpContext, sessions, sessionCookieName, sessionStoreDirectory);
        return TypedResults.Unauthorized();
    }

    var topics = await client.GetAllTopicsAsync();
    var results = topics
        .Select(topic => new ThreadSummaryDto(
            Id: BuildThreadId(topic.Url),
            Title: topic.Title,
            Board: topic.Board,
            Url: topic.Url,
            LastPostBy: topic.LastPostBy,
            LastPostAt: topic.LastPostAt))
        .ToList();

    TouchSession(session);
    return TypedResults.Ok<IReadOnlyList<ThreadSummaryDto>>(results);
});

app.MapGet("/api/threads/{id}", async Task<Results<Ok<ThreadDetailsDto>, NotFound<string>, UnauthorizedHttpResult>> (
    string id,
    HttpContext httpContext) =>
{
    var session = TryGetSession(httpContext, sessions, sessionCookieName, sessionStoreDirectory);
    if (session is null)
        return TypedResults.Unauthorized();

    using var client = new SmfHttpClient(smfBaseUrl);
    if (!client.TryLoadCookies(session.CookieFilePath))
    {
        RemoveSession(httpContext, sessions, sessionCookieName, sessionStoreDirectory);
        return TypedResults.Unauthorized();
    }

    var topics = await client.GetAllTopicsAsync();
    var topic = topics.FirstOrDefault(t => string.Equals(BuildThreadId(t.Url), id, StringComparison.Ordinal));
    if (topic is null)
        return TypedResults.NotFound("Thread not found.");

    var posts = await client.GetTopicAsync(topic.Url);
    var dto = new ThreadDetailsDto(
        Id: id,
        Title: topic.Title,
        Url: topic.Url,
        Posts: posts.Select(p => new PostDto(p.MessageId, p.Author, p.Body, p.PostedAt)).ToList());

    TouchSession(session);
    return TypedResults.Ok(dto);
});

app.MapPost("/api/threads/{id}/replies", async Task<Results<Ok, BadRequest<string>, NotFound<string>, UnauthorizedHttpResult>> (
    string id,
    ReplyRequestDto request,
    HttpContext httpContext) =>
{
    if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Message))
        return TypedResults.BadRequest("Subject and message are required.");

    var session = TryGetSession(httpContext, sessions, sessionCookieName, sessionStoreDirectory);
    if (session is null)
        return TypedResults.Unauthorized();

    using var client = new SmfHttpClient(smfBaseUrl);
    if (!client.TryLoadCookies(session.CookieFilePath))
    {
        RemoveSession(httpContext, sessions, sessionCookieName, sessionStoreDirectory);
        return TypedResults.Unauthorized();
    }

    var topics = await client.GetAllTopicsAsync();
    var topic = topics.FirstOrDefault(t => string.Equals(BuildThreadId(t.Url), id, StringComparison.Ordinal));
    if (topic is null)
        return TypedResults.NotFound("Thread not found.");

    await client.PostReplyAsync(topic.Url, request.Subject, request.Message);
    client.SaveCookies(session.CookieFilePath);

    TouchSession(session);
    return TypedResults.Ok();
});

// Temporary debug endpoint – shows HTML structure of a forum page for parser development.
// Usage: GET /api/debug/html-structure?url=<smf-url>
if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/debug/html-structure", async (string url, HttpContext httpContext) =>
    {
        var session = TryGetSession(httpContext, sessions, sessionCookieName, sessionStoreDirectory);
        if (session is null)
            return Results.Unauthorized();

        using var client = new SmfHttpClient(smfBaseUrl);
        if (!client.TryLoadCookies(session.CookieFilePath))
            return Results.Unauthorized();

        var html = await client.GetRawHtmlAsync(url);
        var info = SmfHtmlParser.DiagnoseHtml(html);
        return Results.Ok(info);
    });
}

app.Run();

static string CreateSessionId()
{
    Span<byte> bytes = stackalloc byte[32];
    RandomNumberGenerator.Fill(bytes);
    return Convert.ToHexString(bytes).ToLowerInvariant();
}

static CookieOptions BuildSessionCookieOptions(HttpContext context)
{
    return new CookieOptions
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Expires = DateTimeOffset.UtcNow.AddHours(8),
    };
}

static SessionState? TryGetSession(
    HttpContext context,
    ConcurrentDictionary<string, SessionState> sessions,
    string cookieName,
    string sessionStoreDirectory)
{
    var sessionId = TryGetSessionIdFromCookie(context, cookieName);
    if (sessionId is null)
        return null;

    if (sessions.TryGetValue(sessionId, out var cachedSession))
    {
        if (!File.Exists(cachedSession.CookieFilePath))
            return null;

        return cachedSession;
    }

    var cookieFilePath = GetSessionCookieFilePath(sessionStoreDirectory, sessionId);
    if (!File.Exists(cookieFilePath))
        return null;

    var restoredSession = new SessionState(cookieFilePath, DateTimeOffset.UtcNow);
    sessions[sessionId] = restoredSession;

    return restoredSession;
}

static string? TryGetSessionIdFromCookie(HttpContext context, string cookieName)
{
    if (!context.Request.Cookies.TryGetValue(cookieName, out var sessionId) || string.IsNullOrWhiteSpace(sessionId))
        return null;

    if (!IsValidSessionId(sessionId))
        return null;

    return sessionId;
}

static void RemoveSession(
    HttpContext context,
    ConcurrentDictionary<string, SessionState> sessions,
    string cookieName,
    string sessionStoreDirectory)
{
    var sessionId = TryGetSessionIdFromCookie(context, cookieName);
    if (sessionId is not null)
    {
        if (sessions.TryRemove(sessionId, out var session)
            && File.Exists(session.CookieFilePath))
        {
            File.Delete(session.CookieFilePath);
        }

        var cookieFilePath = GetSessionCookieFilePath(sessionStoreDirectory, sessionId);
        if (File.Exists(cookieFilePath))
            File.Delete(cookieFilePath);
    }

    context.Response.Cookies.Delete(cookieName);
}

static string GetSessionCookieFilePath(string sessionStoreDirectory, string sessionId)
{
    return Path.Combine(sessionStoreDirectory, sessionId + ".json");
}

static bool IsValidSessionId(string sessionId)
{
    if (sessionId.Length != 64)
        return false;

    foreach (var c in sessionId)
    {
        if (!char.IsAsciiHexDigit(c))
            return false;
    }

    return true;
}

static void TouchSession(SessionState session)
{
    session.LastSeenUtc = DateTimeOffset.UtcNow;
}

static string BuildThreadId(string url)
{
    var topicValue = SmfHtmlParser.ExtractQueryParam(url, "topic");
    if (string.IsNullOrWhiteSpace(topicValue))
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    return topicValue.Split('.')[0];
}

static string? GetBoardIdFromBoardUrl(string url)
{
    var boardValue = SmfHtmlParser.ExtractQueryParam(url, "board");
    if (string.IsNullOrWhiteSpace(boardValue))
        return null;

    return boardValue.Split('.')[0];
}

sealed class SessionState
{
    public SessionState(string cookieFilePath, DateTimeOffset lastSeenUtc)
    {
        CookieFilePath = cookieFilePath;
        LastSeenUtc = lastSeenUtc;
    }

    public string CookieFilePath { get; }

    public DateTimeOffset LastSeenUtc { get; set; }
}
