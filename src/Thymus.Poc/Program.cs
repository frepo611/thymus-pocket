using Microsoft.Extensions.Configuration;
using Thymus.SmfAdapter;
using Thymus.SmfAdapter.Models;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables("SMF_")
    .Build();

var baseUrl       = config["Smf:BaseUrl"]    ?? throw new InvalidOperationException("Smf:BaseUrl is required.");
var username      = config["Smf:Username"];
if (string.IsNullOrWhiteSpace(username))
    throw new InvalidOperationException("Smf:Username is required (set via SMF_Smf__Username).");

var password      = config["Smf:Password"];
if (string.IsNullOrWhiteSpace(password))
    throw new InvalidOperationException("Smf:Password is required (set via SMF_Smf__Password).");

var cookieFileRaw   = config["Smf:CookieFile"] ?? "smf_session.json";
var cookieFile      = Path.IsPathRooted(cookieFileRaw)
    ? cookieFileRaw
    : Path.Combine(AppContext.BaseDirectory, cookieFileRaw);
var enableReplyDemo       = bool.TryParse(config["Smf:EnableReplyDemo"],       out var r)  && r;
var enableCreateTopicDemo = bool.TryParse(config["Smf:EnableCreateTopicDemo"], out var ct) && ct;
var enableLogoutDemo      = bool.TryParse(config["Smf:EnableLogoutDemo"],      out var lo) && lo;

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
            auth = await client.GetArtifactsAsync();
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
Console.WriteLine($"[Session] Cookies saved to: {cookieFile}");

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
Console.WriteLine("=== Create New Topic Demo ===");
var firstBoard = (await client.GetBoardUrlsAsync()).FirstOrDefault();
if (!enableCreateTopicDemo)
{
    Console.WriteLine("(disabled — set Smf:EnableCreateTopicDemo=true in appsettings.json to enable)");
}
else if (firstBoard is null)
{
    Console.WriteLine("(no board found — skipping)");
}
else
{
    Console.WriteLine($"Creating new topic in board: {firstBoard}");
    var newTopicUrl = await client.CreateTopicAsync(firstBoard, "Test topic from Thymus PoC", "This topic was created automatically by the Thymus PoC.");
    Console.WriteLine($"New topic URL: {newTopicUrl ?? "(unknown)"}");
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

Console.WriteLine();
Console.WriteLine("=== Logout Demo ===");
if (!enableLogoutDemo)
{
    Console.WriteLine("(disabled — set Smf:EnableLogoutDemo=true in appsettings.json to enable)");
}
else
{
    var loggedOut = await client.EnsureLoggedOutAsync();
    Console.WriteLine(loggedOut
        ? "Session is logged out."
        : "Logout attempted, but login cookie is still present.");
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
