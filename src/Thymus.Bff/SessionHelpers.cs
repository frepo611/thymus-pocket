using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Thymus.SmfAdapter;

namespace Thymus.Bff;

public static class SessionHelpers
{
    public static string CreateSessionId()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static CookieOptions BuildSessionCookieOptions(HttpContext context)
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

    public static SessionState? TryGetSession(
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
    public static string? TryGetSessionIdFromCookie(HttpContext context, string cookieName)
    {
        if (!context.Request.Cookies.TryGetValue(cookieName, out var sessionId) || string.IsNullOrWhiteSpace(sessionId))
            return null;

        if (!IsValidSessionId(sessionId))
            return null;

        return sessionId;
    }

    public static void RemoveSession(
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

    public static string GetSessionCookieFilePath(string sessionStoreDirectory, string sessionId)
    {
        return Path.Combine(sessionStoreDirectory, sessionId + ".json");
    }

    public static bool IsValidSessionId(string sessionId)
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

    public static void TouchSession(SessionState session)
    {
        session.LastSeenUtc = DateTimeOffset.UtcNow;
    }

    public static string BuildThreadId(string url)
    {
        var topicValue = SmfHtmlParser.ExtractQueryParam(url, "topic");
        if (string.IsNullOrWhiteSpace(topicValue))
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        return topicValue.Split('.')[0];
    }

    public static string? GetBoardIdFromBoardUrl(string url)
    {
        var boardValue = SmfHtmlParser.ExtractQueryParam(url, "board");
        if (string.IsNullOrWhiteSpace(boardValue))
            return null;

        return boardValue.Split('.')[0];
    }
}