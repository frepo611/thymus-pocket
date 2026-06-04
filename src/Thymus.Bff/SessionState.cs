namespace Thymus.Bff;

public sealed class SessionState
{
    public SessionState(string cookieFilePath, DateTimeOffset lastSeenUtc)
    {
        CookieFilePath = cookieFilePath;
        LastSeenUtc = lastSeenUtc;
    }

    public string CookieFilePath { get; }

    public DateTimeOffset LastSeenUtc { get; set; }
}