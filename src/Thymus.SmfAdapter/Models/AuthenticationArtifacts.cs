namespace Thymus.SmfAdapter.Models;

public sealed record AuthenticationArtifacts(
    CookieArtifact? MainLoginCookie,
    CookieArtifact? PhpSessionCookie,
    CookieArtifact? TfaCookie,
    string? SessionVar,
    string? SessionId,
    IReadOnlyList<CookieArtifact> AllCookies);

public sealed record CookieArtifact(
    string Name,
    string RawValue,
    string? DecodedValue,
    SmfCookiePayload? Payload);

public sealed record SmfCookiePayload(
    string? Entry0,
    string? Entry1,
    long? Entry2,
    string? Entry3,
    string? Entry4);
