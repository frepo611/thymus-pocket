using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Thymus.Poc.Models;

namespace Thymus.Poc;

public sealed class SmfHttpClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly CookieContainer _cookieContainer;

    public SmfHttpClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');

        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            UseCookies = true,
            AllowAutoRedirect = true,
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(_baseUrl + "/"),
        };

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0 Safari/537.36 Thymus-Poc/1.0");
    }

    public async Task<AuthenticationArtifacts> LoginAsync(string username, string password)
    {
        using var loginPageResponse = await GetActionAsync("login");
        loginPageResponse.EnsureSuccessStatusCode();

        var loginPageHtml = await loginPageResponse.Content.ReadAsStringAsync();
        var (actionUrl, hiddenFields) = ExtractLoginForm(loginPageHtml);

        var form = new Dictionary<string, string>(hiddenFields, StringComparer.Ordinal)
        {
            ["user"] = username,
            ["passwrd"] = password,
            ["cookielength"] = "-1",
        };

        using var response = await PostAsync(actionUrl, form);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();

        if (body.Contains("action=login", StringComparison.OrdinalIgnoreCase) &&
            body.Contains("passwrd", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Login failed — forum returned the login form again. " +
                "Check username and password.");
        }

        var authenticatedHtml = string.IsNullOrWhiteSpace(body)
            ? await GetHtmlAsync()
            : body;

        var artifacts = ExtractAuthenticationArtifacts(authenticatedHtml);

        Console.WriteLine("[SmfHttpClient] Login successful.");
        return artifacts;
    }

    public async Task<List<ThreadSummary>> GetUnreadTopicsAsync()
    {
        var html = await GetHtmlAsync("unread");
        return ParseThreadList(html);
    }

    public async Task<List<ThreadSummary>> GetRecentPostsAsync()
    {
        var html = await GetHtmlAsync("recent");
        return ParseThreadList(html);
    }

    public async Task<List<ThreadSummary>> GetAllTopicsAsync()
    {
        var homeHtml = await GetHtmlAsync();
        var boardUrls = ParseBoardUrls(homeHtml);
        var results = new List<ThreadSummary>();

        foreach (var boardUrl in boardUrls)
        {
            var boardHtml = await _http.GetStringAsync(boardUrl);
            results.AddRange(ParseThreadList(boardHtml));
        }

        return results
            .DistinctBy(topic => topic.Url)
            .ToList();
    }

    public async Task<List<PostContent>> GetTopicAsync(string topicUrl)
    {
        var html = await _http.GetStringAsync(topicUrl);
        return ParseTopic(html);
    }

    public async Task<string?> CreateTopicAsync(string boardUrl, string subject, string message)
    {
        var boardParam = ExtractQueryParam(boardUrl, "board")
            ?? throw new ArgumentException("Could not extract board parameter from URL.", nameof(boardUrl));

        // SMF uses only the numeric board id (drop the .0 page offset if present)
        var boardId = boardParam.Split('.')[0];

        var newTopicFormUrl = $"index.php?action=post;board={Uri.EscapeDataString(boardId + ".0")}";
        var formHtml = await _http.GetStringAsync(newTopicFormUrl);
        var (actionUrl, hiddenFields) = ExtractFormFields(formHtml, "post2");

        var form = new Dictionary<string, string>(hiddenFields, StringComparer.Ordinal)
        {
            ["subject"] = subject,
            ["message"] = message,
            ["post"]    = "Post",
        };

        using var response = await PostAsync(actionUrl, form);
        response.EnsureSuccessStatusCode();

        // SMF redirects to the new topic after a successful post;
        // the final URL contains the topic id.
        var topicUrl = response.RequestMessage?.RequestUri?.ToString();
        Console.WriteLine($"[SmfHttpClient] Topic created. URL: {topicUrl ?? "(unknown)"}");
        return topicUrl;
    }

    public async Task PostReplyAsync(string topicUrl, string subject, string message)
    {
        var topicParam = ExtractQueryParam(topicUrl, "topic")
            ?? throw new ArgumentException("Could not extract topic parameter from URL.", nameof(topicUrl));

        var replyFormUrl = $"index.php?action=post;topic={Uri.EscapeDataString(topicParam)}";
        var formHtml = await _http.GetStringAsync(replyFormUrl);
        var (actionUrl, hiddenFields) = ExtractFormFields(formHtml, "post2");

        var form = new Dictionary<string, string>(hiddenFields, StringComparer.Ordinal)
        {
            ["subject"] = subject,
            ["message"] = message,
            ["post"] = "Post",
        };

        using var response = await PostAsync(actionUrl, form);
        response.EnsureSuccessStatusCode();

        Console.WriteLine("[SmfHttpClient] Reply posted successfully.");
    }

    public async Task<List<string>> GetBoardUrlsAsync()
    {
        var html = await GetHtmlAsync();
        return ParseBoardUrls(html);
    }

    public async Task<bool> LogoutAsync()
    {
        var homeHtml = await GetHtmlAsync();
        var artifacts = ExtractAuthenticationArtifacts(homeHtml);

        var logoutUrl = ExtractLogoutUrl(homeHtml)
            ?? BuildLogoutUrl(artifacts);

        if (string.IsNullOrWhiteSpace(logoutUrl))
            throw new InvalidOperationException("Could not find a logout URL in the current SMF page.");

        using var response = await _http.GetAsync(logoutUrl);
        response.EnsureSuccessStatusCode();

        var hasLoginCookie = GetCookies().Any(c =>
            c.Name.StartsWith("SMFCookie", StringComparison.OrdinalIgnoreCase) &&
            !c.Name.EndsWith("_tfa", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine(hasLoginCookie
            ? "[SmfHttpClient] Logout requested, but login cookie still present."
            : "[SmfHttpClient] Logout successful.");

        return !hasLoginCookie;
    }

    public async Task<bool> EnsureLoggedOutAsync()
    {
        var hasLoginCookie = GetCookies().Any(c =>
            c.Name.StartsWith("SMFCookie", StringComparison.OrdinalIgnoreCase) &&
            !c.Name.EndsWith("_tfa", StringComparison.OrdinalIgnoreCase));

        if (!hasLoginCookie)
            return true;

        return await LogoutAsync();
    }

    public async Task<AuthenticationArtifacts> GetArtifactsAsync()
    {
        var html = await GetHtmlAsync();
        return ExtractAuthenticationArtifacts(html);
    }

    private Task<HttpResponseMessage> GetActionAsync(string action)
        => _http.GetAsync(BuildActionUrl(action));

    private Task<HttpResponseMessage> PostAsync(string url, IReadOnlyDictionary<string, string> formValues)
        => _http.PostAsync(url, new FormUrlEncodedContent(formValues));

    private Task<string> GetHtmlAsync(string? action = null)
        => _http.GetStringAsync(string.IsNullOrWhiteSpace(action) ? "index.php" : BuildActionUrl(action));

    private static string BuildActionUrl(string action)
        => $"index.php?action={Uri.EscapeDataString(action)}";

    private static (string ActionUrl, IReadOnlyDictionary<string, string> HiddenFields) ExtractLoginForm(string html)
    {
        try
        {
            return ExtractFormFields(html, "login2");
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                "Could not find SMF login form (action=login2). The page structure may have changed.", ex);
        }
    }

    private static (string ActionUrl, IReadOnlyDictionary<string, string> HiddenFields) ExtractFormFields(
        string html, string actionContains)
    {
        var document = ParseDocument(html);
        var form = document.QuerySelectorAll("form[action]")
            .FirstOrDefault(f =>
            {
                var attr = f.GetAttribute("action");
                return !string.IsNullOrWhiteSpace(attr)
                    && attr.Contains($"action={actionContains}", StringComparison.OrdinalIgnoreCase);
            });

        if (form is null)
            throw new InvalidOperationException(
                $"Could not find SMF form with action={actionContains}. The page structure may have changed.");

        var action = form.GetAttribute("action")
            ?? throw new InvalidOperationException($"SMF form ({actionContains}) has no action URL.");

        var hiddenFields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in form.QuerySelectorAll("input[type='hidden'][name]"))
        {
            var name = input.GetAttribute("name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            hiddenFields[name] = input.GetAttribute("value") ?? string.Empty;
        }

        return (action, hiddenFields);
    }

    private AuthenticationArtifacts ExtractAuthenticationArtifacts(string html)
    {
        var cookies = GetCookies()
            .Select(ToCookieArtifact)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var mainLoginCookie = cookies.FirstOrDefault(c =>
            c.Name.StartsWith("SMFCookie", StringComparison.OrdinalIgnoreCase) &&
            !c.Name.EndsWith("_tfa", StringComparison.OrdinalIgnoreCase));

        var phpSessionCookie = cookies.FirstOrDefault(c =>
            c.Name.Equals("PHPSESSID", StringComparison.OrdinalIgnoreCase));

        var tfaCookie = cookies.FirstOrDefault(c =>
            c.Name.StartsWith("SMFCookie", StringComparison.OrdinalIgnoreCase) &&
            c.Name.EndsWith("_tfa", StringComparison.OrdinalIgnoreCase));

        return new AuthenticationArtifacts(
            mainLoginCookie,
            phpSessionCookie,
            tfaCookie,
            ExtractSessionVar(html),
            ExtractSessionId(html),
            cookies);
    }

    private List<Cookie> GetCookies()
    {
        var baseUri = new Uri(_baseUrl + "/");
        var cookies = _cookieContainer.GetCookies(baseUri);
        return cookies.Cast<Cookie>().ToList();
    }

    private static CookieArtifact ToCookieArtifact(Cookie cookie)
    {
        var decodedValue = Uri.UnescapeDataString(cookie.Value);
        return new CookieArtifact(
            cookie.Name,
            cookie.Value,
            decodedValue,
            TryParseSmfCookiePayload(decodedValue));
    }

    private static SmfCookiePayload? TryParseSmfCookiePayload(string? decodedValue)
    {
        if (string.IsNullOrWhiteSpace(decodedValue))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(decodedValue);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var root = doc.RootElement;
            return new SmfCookiePayload(
                GetString(root, "0"),
                GetString(root, "1"),
                GetInt64(root, "2"),
                GetString(root, "3"),
                GetString(root, "4"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var property) ? property.ToString() : null;

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number))
            return number;

        if (property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out number))
            return number;

        return null;
    }

    private static string? ExtractSessionVar(string html)
    {
        var document = ParseDocument(html);
        var inputValue = document.QuerySelector("input[name='session_var']")?.GetAttribute("value");
        if (!string.IsNullOrWhiteSpace(inputValue))
            return inputValue;

        var scripts = string.Join('\n', document.QuerySelectorAll("script").Select(s => s.TextContent));
        var match = Regex.Match(
            scripts,
            "smf_session_var\\s*=\\s*['\"](?<value>[^'\"]+)['\"]",
            RegexOptions.IgnoreCase);

        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string? ExtractSessionId(string html)
    {
        var document = ParseDocument(html);
        var inputValue = document.QuerySelector("input[name='session_value']")?.GetAttribute("value")
            ?? document.QuerySelector("input[name='session_id']")?.GetAttribute("value");
        if (!string.IsNullOrWhiteSpace(inputValue))
            return inputValue;

        var scripts = string.Join('\n', document.QuerySelectorAll("script").Select(s => s.TextContent));
        var match = Regex.Match(
            scripts,
            "smf_session_id\\s*=\\s*['\"](?<value>[^'\"]+)['\"]",
            RegexOptions.IgnoreCase);

        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string? ExtractLogoutUrl(string html)
    {
        var document = ParseDocument(html);
        return document.QuerySelector("a[href*='action=logout']")?.GetAttribute("href");
    }

    private static string? BuildLogoutUrl(AuthenticationArtifacts artifacts)
    {
        if (string.IsNullOrWhiteSpace(artifacts.SessionVar) || string.IsNullOrWhiteSpace(artifacts.SessionId))
            return null;

        return $"index.php?action=logout;{artifacts.SessionVar}={artifacts.SessionId}";
    }

    private List<ThreadSummary> ParseThreadList(string html)
    {
        var document = ParseDocument(html);
        var results = new List<ThreadSummary>();

        foreach (var row in document.QuerySelectorAll("tr"))
        {
            var subjectCell = row.QuerySelector("td.subject");
            if (subjectCell is null)
                continue;

            var subjectLink = subjectCell.QuerySelector("a[href]");
            if (subjectLink is null)
                continue;

            var title = NormalizeWhitespace(subjectLink.TextContent);
            var url = subjectLink.GetAttribute("href") ?? string.Empty;

            var boardCell = row.QuerySelector("td.board");
            var boardName = NormalizeWhitespace(
                boardCell?.QuerySelector("a")?.TextContent
                ?? boardCell?.TextContent);

            var lastPostCell = row.QuerySelector("td.lastpost");
            var lastPostBy = NormalizeWhitespace(lastPostCell?.QuerySelector("a")?.TextContent);
            if (string.IsNullOrWhiteSpace(lastPostBy))
                lastPostBy = null;

            var lastPostCellText = NormalizeWhitespace(lastPostCell?.TextContent);
            if (string.IsNullOrWhiteSpace(lastPostCellText))
                lastPostCellText = null;

            results.Add(new ThreadSummary(title, boardName, url, lastPostBy, lastPostCellText));
        }

        foreach (var topicCard in document.QuerySelectorAll("#topic_container > .windowbg"))
        {
            var subjectLink = topicCard.QuerySelector(".message_index_title span[id^='msg_'] a[href*='topic=']")
                ?? topicCard.QuerySelector(".message_index_title a[href*='topic=']");
            if (subjectLink is null)
                continue;

            var title = NormalizeWhitespace(subjectLink.TextContent);
            var url = subjectLink.GetAttribute("href") ?? string.Empty;

            var boardName = NormalizeWhitespace(
                document.QuerySelector(".display_title")?.TextContent
                ?? document.QuerySelector(".navigate_section li.last span")?.TextContent);

            var lastPostSection = topicCard.QuerySelector(".lastpost");
            var lastPostBy = NormalizeWhitespace(
                lastPostSection?.QuerySelector("a[href*='action=profile']")?.TextContent);
            if (string.IsNullOrWhiteSpace(lastPostBy))
                lastPostBy = null;

            var lastPostCellText = NormalizeWhitespace(lastPostSection?.TextContent);
            if (string.IsNullOrWhiteSpace(lastPostCellText))
                lastPostCellText = null;

            if (results.Any(existing => string.Equals(existing.Url, url, StringComparison.OrdinalIgnoreCase)))
                continue;

            results.Add(new ThreadSummary(title, boardName, url, lastPostBy, lastPostCellText));
        }

        return results;
    }

    private List<string> ParseBoardUrls(string html)
    {
        var document = ParseDocument(html);

        return document.QuerySelectorAll("a.subject[href*='board=']")
            .Select(link => link.GetAttribute("href"))
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .Select(href => href!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<PostContent> ParseTopic(string html)
    {
        var document = ParseDocument(html);
        var results = new List<PostContent>();

        foreach (var card in document.QuerySelectorAll("#forumposts .windowbg"))
        {
            var innerEl = card.QuerySelector(".post .inner[data-msgid]");
            if (innerEl is null)
                continue;

            int.TryParse(innerEl.GetAttribute("data-msgid"), out var messageId);

            var author = NormalizeWhitespace(
                card.QuerySelector(".poster h4 a")?.TextContent
                ?? card.QuerySelector(".poster h4")?.TextContent);

            var body = NormalizeWhitespace(innerEl.TextContent);

            var dateText = card.QuerySelector(".postinfo a[rel='nofollow']")?.TextContent;
            DateTimeOffset? postedAt = null;
            if (!string.IsNullOrWhiteSpace(dateText)
                && DateTimeOffset.TryParse(
                    dateText,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var parsed))
            {
                postedAt = parsed;
            }

            results.Add(new PostContent(messageId, author, body, postedAt));
        }

        return results;
    }

    private static string? ExtractQueryParam(string url, string paramName)
    {
        // SMF uses both & and ; as query string separators
        var queryStart = url.IndexOf('?');
        if (queryStart < 0)
            return null;

        var queryPart = url[(queryStart + 1)..];
        foreach (var part in queryPart.Split(new[] { '&', ';' }))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2
                && string.Equals(kv[0], paramName, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(kv[1]);
            }
        }

        return null;
    }

    private static IDocument ParseDocument(string html)
    {
        var parser = new HtmlParser();
        return parser.ParseDocument(html);
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));
    }

    public void SaveCookies(string filePath)
    {
        var baseUri = new Uri(_baseUrl + "/");
        var cookies = _cookieContainer.GetCookies(baseUri)
            .Cast<Cookie>()
            .Select(c => new { c.Name, c.Value, c.Domain, c.Path, c.Expires })
            .ToList();

        File.WriteAllText(filePath, System.Text.Json.JsonSerializer.Serialize(cookies));
    }

    public bool TryLoadCookies(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var name    = entry.GetProperty("Name").GetString() ?? string.Empty;
                var value   = entry.GetProperty("Value").GetString() ?? string.Empty;
                var domain  = entry.GetProperty("Domain").GetString() ?? string.Empty;
                var path    = entry.GetProperty("Path").GetString() ?? "/";

                _cookieContainer.Add(new Cookie(name, value, path, domain));
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
