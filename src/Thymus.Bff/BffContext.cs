using System.Collections.Concurrent;

namespace Thymus.Bff;

public class BffContext
{
    public required string SmfBaseUrl { get; init; }
    public required ConcurrentDictionary<string, SessionState> Sessions { get; init; }
    public required string SessionCookieName { get; init; }
    public required string SessionStoreDirectory { get; init; }

}