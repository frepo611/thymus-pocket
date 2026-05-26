using Microsoft.Extensions.Configuration;
using Thymus.Poc;
using Thymus.Poc.Models;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables("SMF_")
    .Build();

var baseUrl       = config["Smf:BaseUrl"]    ?? throw new InvalidOperationException("Smf:BaseUrl is required.");
var username      = config["Smf:Username"]   ?? throw new InvalidOperationException("Smf:Username is required.");
var password      = config["Smf:Password"]   ?? throw new InvalidOperationException("Smf:Password is required.");
var cookieFile    = config["Smf:CookieFile"] ?? "smf_session.json";
var enableReplyDemo = bool.TryParse(config["Smf:EnableReplyDemo"], out var r) && r;

Console.WriteLine($"Connecting to {baseUrl} ...");

using var client = new SmfHttpClient(baseUrl);

bool sessionLoaded = client.TryLoadCookies(cookieFile);
if (sessionLoaded)
    Console.WriteLine("[Session] Loaded cookies from disk.");

AuthenticationArtifacts auth;
try
{
    if (sessionLoaded)
    {
        // Validate stored session with a cheap call; re-login on failure
        try
        {
            await client.GetAllTopicsAsync();
            // Session is valid — produce a minimal artifacts object via login page
            auth = await client.LoginAsync(username, password);
        }
        catch (HttpRequestException)
        {
            Console.WriteLine("[Session] Stored session expired — re-authenticating.");
            auth = await client.LoginAsync(username, password);
        }
    }
    else
    {
        auth = await client.LoginAsync(username, password);
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Login failed: {ex.Message}");
    return;
}

client.SaveCookies(cookieFile);
Console.WriteLine("[Session] Cookies saved to disk.");

Console.WriteLine();
Console.WriteLine("=== Authentication Artifacts ===");
PrintCookie("Main login cookie", auth.MainLoginCookie);
PrintCookie("PHP session cookie", auth.PhpSessionCookie);
PrintCookie("2FA cookie", auth.TfaCookie);
Console.WriteLine($"session_var: {auth.SessionVar ?? "(not found)"}");
Console.WriteLine($"session_id: {auth.SessionId ?? "(not found)"}");

if (auth.AllCookies.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("All cookies in jar:");
    foreach (var cookie in auth.AllCookies)
    {
        Console.WriteLine($"- {cookie.Name} = {cookie.RawValue}");
    }
}

Console.WriteLine();
Console.WriteLine("=== Unread Topics ===");
var unread = await client.GetUnreadTopicsAsync();

if (unread.Count == 0)
{
    Console.WriteLine("(no unread topics)");
}
else
{
    foreach (var t in unread)
    {
        Console.WriteLine($"[{t.Board}] {t.Title}");
        if (!string.IsNullOrEmpty(t.LastPostBy))
            Console.WriteLine($"  Last post by: {t.LastPostBy}");
    }
}

Console.WriteLine();
Console.WriteLine("=== Recent Posts ===");
var recent = await client.GetRecentPostsAsync();

if (recent.Count == 0)
{
    Console.WriteLine("(no recent posts)");
}
else
{
    foreach (var t in recent.Take(10))
        Console.WriteLine($"[{t.Board}] {t.Title}");
}

Console.WriteLine();
Console.WriteLine("=== All Known Topics ===");
var allTopics = await client.GetAllTopicsAsync();

if (allTopics.Count == 0)
{
    Console.WriteLine("(no topics found)");
}
else
{
    foreach (var t in allTopics)
    {
        Console.WriteLine($"[{t.Board}] {t.Title}");
        if (!string.IsNullOrEmpty(t.LastPostBy))
            Console.WriteLine($"  Last post by: {t.LastPostBy}");
    }
}

Console.WriteLine();
Console.WriteLine("=== Topic Content (first available thread) ===");
var firstThread = allTopics.FirstOrDefault() ?? unread.FirstOrDefault() ?? recent.FirstOrDefault();
if (firstThread is null)
{
    Console.WriteLine("(no topics available to read)");
}
else
{
    Console.WriteLine($"Thread: {firstThread.Title}");
    var posts = await client.GetTopicAsync(firstThread.Url);
    if (posts.Count == 0)
    {
        Console.WriteLine("(no posts parsed — HTML structure may differ from expected)");
    }
    else
    {
        foreach (var post in posts)
        {
            Console.WriteLine($"  [{post.MessageId}] {post.Author}  {post.PostedAt?.ToString("yyyy-MM-dd HH:mm") ?? "?"}");
            Console.WriteLine($"    {post.Body[..Math.Min(120, post.Body.Length)]}...");
        }
    }
}

Console.WriteLine();
Console.WriteLine("=== Post Reply Demo ===");
if (!enableReplyDemo)
{
    Console.WriteLine("(disabled — set Smf:EnableReplyDemo=true in appsettings.json to enable)");
}
else if (firstThread is null)
{
    Console.WriteLine("(no thread to reply to — skipping)");
}
else
{
    Console.WriteLine($"Posting reply to: {firstThread.Title}");
    await client.PostReplyAsync(firstThread.Url, "Re: " + firstThread.Title, "Test reply from Thymus PoC.");
}

static void PrintCookie(string label, CookieArtifact? cookie)
{
    Console.WriteLine($"{label}: {(cookie is null ? "(not found)" : cookie.Name)}");
    if (cookie is null)
        return;

    Console.WriteLine($"  raw: {cookie.RawValue}");

    if (!string.Equals(cookie.DecodedValue, cookie.RawValue, StringComparison.Ordinal))
        Console.WriteLine($"  decoded: {cookie.DecodedValue}");

    if (cookie.Payload is not null)
    {
        Console.WriteLine($"  payload[0]: {cookie.Payload.Entry0}");
        Console.WriteLine($"  payload[1]: {cookie.Payload.Entry1}");
        Console.WriteLine($"  payload[2]: {cookie.Payload.Entry2}");
        Console.WriteLine($"  payload[3]: {cookie.Payload.Entry3}");
        Console.WriteLine($"  payload[4]: {cookie.Payload.Entry4}");
    }
}
