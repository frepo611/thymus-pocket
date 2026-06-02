using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Thymus.SmfAdapter.Models;

namespace Thymus.SmfAdapter;

public static class SmfHtmlParser
{
    public static object DiagnoseHtml(string html)
    {
        var doc = ParseDocument(html);

        var pageTitle = doc.QuerySelector("title")?.TextContent;
        var navSection = doc.QuerySelector(".navigate_section")?.InnerHtml[..Math.Min(400, doc.QuerySelector(".navigate_section")?.InnerHtml.Length ?? 0)];

        // Pagination links
        var topicLinks = doc.QuerySelectorAll("a[href*='topic=']")
            .Take(20).Select(e => e.GetAttribute("href")).ToList();
        var boardLinks = doc.QuerySelectorAll("a[href*='board=']")
            .Take(20).Select(e => e.GetAttribute("href")).ToList();
        var pagenavLinks = doc.QuerySelectorAll(".pagelinks a, #pagelinks a, .pagenav a, .navigate_section a")
            .Take(20).Select(e => e.GetAttribute("href")).ToList();

        // Post container candidates
        var forumpostsChildren = doc.QuerySelectorAll("#forumposts > *")
            .Take(5).Select(e => $"<{e.TagName.ToLower()} id='{e.GetAttribute("id")}' class='{e.GetAttribute("class")}'>").ToList();
        var windowbgCount = doc.QuerySelectorAll("#forumposts .windowbg").Length;
        var windowbg2Count = doc.QuerySelectorAll("#forumposts .windowbg2").Length;
        var msgDivs = doc.QuerySelectorAll("div[id^='msg_']")
            .Take(5).Select(e => $"id={e.GetAttribute("id")} class={e.GetAttribute("class")}").ToList();
        var postInner = doc.QuerySelectorAll(".post .inner").Take(3).Select(e => e.OuterHtml[..Math.Min(200, e.OuterHtml.Length)]).ToList();
        var postbodyEls = doc.QuerySelectorAll(".postbody").Take(3).Select(e => e.OuterHtml[..Math.Min(200, e.OuterHtml.Length)]).ToList();

        // Topic rows in board index
        var topicRows = doc.QuerySelectorAll("tr td.subject span[id^='msg_'] > a[href*='topic=']")
            .Take(5).Select(e => $"{e.TextContent.Trim()} => {e.GetAttribute("href")}").ToList();
        var topicCards = doc.QuerySelectorAll("#topic_container .windowbg").Length;

        return new
        {
            pageTitle,
            navSection,
            pagination = new { topicLinks, boardLinks, pagenavLinks },
            posts = new
            {
                windowbgCount,
                windowbg2Count,
                forumpostsChildren,
                msgDivs,
                postInner,
                postbodyEls,
            },
            boardTopics = new { topicRows, topicCards },
        };
    }

    public static List<ThreadSummary> ParseThreadList(string html)
    {
        var document = ParseDocument(html);
        var results = new List<ThreadSummary>();

        foreach (var row in document.QuerySelectorAll("tr"))
        {
            var subjectCell = row.QuerySelector("td.subject");
            if (subjectCell is null)
                continue;

            var subjectLink = subjectCell.QuerySelector("span[id^='msg_'] > a[href]");
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

    public static List<string> ParseBoardUrls(string html)
    {
        var document = ParseDocument(html);

        return document.QuerySelectorAll("a[href*='board=']")
            .Select(link => link.GetAttribute("href"))
            .Where(href => !string.IsNullOrWhiteSpace(href) && !href.Contains("action=unread", StringComparison.OrdinalIgnoreCase))
            .Select(href => href!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<(string Category, string Name, string Url)> ParseBoards(string html)
    {
        var document = ParseDocument(html);
        var results = new List<(string Category, string Name, string Url)>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Prefer canonical SMF board index structure: category header tbody + category boards tbody.
        foreach (var categoryBody in document.QuerySelectorAll("tbody[id$='_boards']"))
        {
            var categoryId = categoryBody.GetAttribute("id")?.Replace("_boards", "", StringComparison.Ordinal);
            var categoryName = string.Empty;
            if (!string.IsNullOrWhiteSpace(categoryId))
            {
                var categoryHeader = document.QuerySelector($"tbody#{categoryId} h3.catbg");
                categoryName = CleanCategoryText(categoryHeader?.TextContent);
            }

            foreach (var boardLink in categoryBody.QuerySelectorAll("a[href*='board=']"))
            {
                var href = boardLink.GetAttribute("href");
                if (string.IsNullOrWhiteSpace(href))
                    continue;
                if (ExtractQueryParam(href, "board") is null)
                    continue;
                if (href.Contains("action=unread", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!seenUrls.Add(href))
                    continue;

                var name = NormalizeWhitespace(boardLink.TextContent);
                if (!string.IsNullOrWhiteSpace(name))
                    results.Add((categoryName, name, href));
            }
        }

        if (results.Count > 0)
            return results;

        // 2) Fallback for custom themes: track heading text in DOM order.
        var currentCategory = string.Empty;
        foreach (var node in document.QuerySelectorAll("h1, h2, h3, h4, h5, h6, a[href*='board=']"))
        {
            if (!node.Matches("a[href*='board=']"))
            {
                // Ignore headings that are themselves board containers.
                if (node.QuerySelector("a[href*='board=']") is not null)
                    continue;

                var headerText = CleanCategoryText(node.TextContent);
                if (!string.IsNullOrWhiteSpace(headerText))
                    currentCategory = headerText;
                continue;
            }

            var href = node.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
                continue;
            if (ExtractQueryParam(href, "board") is null)
                continue;
            if (href.Contains("action=unread", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!seenUrls.Add(href))
                continue;

            var name = NormalizeWhitespace(node.TextContent);
            if (!string.IsNullOrWhiteSpace(name))
                results.Add((currentCategory, name, href));
        }

        // 3) Last-resort fallback: board names only.
        if (results.Count == 0)
        {
            foreach (var link in document.QuerySelectorAll("a[href*='board=']"))
            {
                var href = link.GetAttribute("href");
                if (string.IsNullOrWhiteSpace(href))
                    continue;
                if (href.Contains("action=unread", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!seenUrls.Add(href))
                    continue;

                var name = NormalizeWhitespace(link.TextContent);
                if (!string.IsNullOrWhiteSpace(name))
                    results.Add((string.Empty, name, href));
            }
        }

        return results;
    }

    public static string ParseTopicTitle(string html)
    {
        var document = ParseDocument(html);
        // SMF puts topic title in .navigate_section h3, or <title>
        var navTitle = document.QuerySelector(".navigate_section h3");
        if (navTitle is not null)
            return NormalizeWhitespace(navTitle.TextContent) ?? string.Empty;

        var titleEl = document.QuerySelector("title");
        if (titleEl is not null)
        {
            var t = NormalizeWhitespace(titleEl.TextContent) ?? string.Empty;
            // Strip common suffix like " - Forum Name"
            var sep = t.LastIndexOf(" - ", StringComparison.Ordinal);
            return sep > 0 ? t[..sep].Trim() : t;
        }

        return string.Empty;
    }

    public static List<PostContent> ParseTopic(string html)
    {
        var document = ParseDocument(html);
        var results = new List<PostContent>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var cards = document.QuerySelectorAll("#forumposts .windowbg, #forumposts .windowbg2, #forumposts > div[id^='msg_']");
        if (cards.Length == 0)
        {
            // Fallback for custom themes that do not keep the default #forumposts/.windowbg structure.
            cards = document.QuerySelectorAll("div[id^='msg_'], .post_wrapper, .windowbg, .windowbg2");
        }

        foreach (var card in cards)
        {
            var innerEl = card.QuerySelector(".post .inner[data-msgid], .post .inner[id^='msg_'], .post .inner, .postbody, .message");
            if (innerEl is null)
                continue;

            var messageIdText = innerEl.GetAttribute("data-msgid")
                ?? innerEl.GetAttribute("id")
                ?? card.GetAttribute("id");

            if (!string.IsNullOrWhiteSpace(messageIdText)
                && messageIdText.StartsWith("msg_", StringComparison.OrdinalIgnoreCase))
            {
                messageIdText = messageIdText[4..];
            }

            int.TryParse(messageIdText, out var messageId);

            var author = NormalizeWhitespace(
                card.QuerySelector(".poster h4 a")?.TextContent
                ?? card.QuerySelector(".poster h4")?.TextContent
                ?? card.QuerySelector(".poster .name")?.TextContent);

            var body = NormalizeWhitespace(innerEl.TextContent);
            var dedupeKey = $"{messageId}|{body}";
            if (!seen.Add(dedupeKey))
                continue;

            var dateText = card.QuerySelector(".postinfo a[rel='nofollow']")?.TextContent
                ?? card.QuerySelector(".keyinfo .smalltext")?.TextContent;
            DateTimeOffset? postedAt = null;
            if (!string.IsNullOrWhiteSpace(dateText)
                && DateTimeOffset.TryParse(
                    dateText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                postedAt = parsed;
            }

            results.Add(new PostContent(messageId, author, body, postedAt));
        }

        return results;
    }

    public static string? ExtractQueryParam(string url, string paramName)
    {
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

    public static int? GetNextPostStart(string html, string topicId, int currentStart)
    {
        var starts = ExtractTopicPageStarts(html, topicId);
        var nextStarts = starts.Where(start => start > currentStart).ToList();
        return nextStarts.Count == 0 ? null : nextStarts.Min();
    }

    public static int? GetPreviousPostStart(string html, string topicId, int currentStart)
    {
        var starts = ExtractTopicPageStarts(html, topicId);
        var previousStarts = starts.Where(start => start < currentStart).ToList();
        return previousStarts.Count == 0 ? null : previousStarts.Max();
    }

    public static int GetLatestPostStart(string html, string topicId)
    {
        var starts = ExtractTopicPageStarts(html, topicId);
        return starts.Count == 0 ? 0 : starts.Max();
    }

    public static List<int> ExtractTopicPageStarts(string html, string topicId)
    {
        var document = ParseDocument(html);
        var starts = new HashSet<int> { 0 };

        foreach (var link in document.QuerySelectorAll("a[href]"))
        {
            var href = link.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
                continue;

            // Skip action links like report/post/notify that may contain topic={id}.1 etc.
            if (href.Contains("action=", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TryExtractSmfPageOffset(href, "topic", out var hrefTopicId, out var start))
                continue;

            if (!string.Equals(hrefTopicId, topicId, StringComparison.Ordinal))
                continue;

            starts.Add(start);
        }

        // Some SMF themes emit thread pagination links with only start=... in the URL.
        foreach (var link in document.QuerySelectorAll(".pagelinks a, #pagelinks a, .pagenav a, .navigate_section a"))
        {
            var href = link.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
                continue;

            if (href.Contains("board=", StringComparison.OrdinalIgnoreCase)
                || href.Contains("board,", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryExtractStartOnly(href, out var start))
                starts.Add(start);
        }

        return starts.OrderBy(start => start).ToList();
    }

    public static int? GetNextBoardStart(string html, string boardId, int currentStart)
    {
        var document = ParseDocument(html);
        var nextStarts = new HashSet<int>();

        foreach (var link in document.QuerySelectorAll("a[href]"))
        {
            var href = link.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
                continue;

            if (!TryExtractSmfPageOffset(href, "board", out var hrefBoardId, out var start))
                continue;

            if (!string.Equals(hrefBoardId, boardId, StringComparison.Ordinal))
                continue;

            if (start > currentStart)
                nextStarts.Add(start);
        }

        // Some SMF themes emit pagination links with only start=... in the URL.
        foreach (var link in document.QuerySelectorAll(".pagelinks a, #pagelinks a, .pagenav a, .navigate_section a"))
        {
            var href = link.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
                continue;

            if (href.Contains("topic=", StringComparison.OrdinalIgnoreCase)
                || href.Contains("topic,", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryExtractStartOnly(href, out var start))
                continue;

            if (start > currentStart)
                nextStarts.Add(start);
        }

        return nextStarts.Count == 0 ? null : nextStarts.Min();
    }

    static bool TryExtractSmfPageOffset(string url, string paramName, out string id, out int start)
    {
        id = string.Empty;
        start = 0;

        var paramValue = ExtractQueryParam(url, paramName);
        if (!string.IsNullOrWhiteSpace(paramValue)
            && TryExtractIdAndStart(paramValue, out id, out start))
        {
            return true;
        }

        // Supports URL forms like index.php?topic=704.20 and index.php/topic,704.20.html
        var match = Regex.Match(
            url,
            $@"(?:^|[/?&;]){Regex.Escape(paramName)}(?:=|,)(?<id>\d+)(?:\.(?<start>\d+))?",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return false;

        id = match.Groups["id"].Value;
        if (match.Groups["start"].Success && int.TryParse(match.Groups["start"].Value, out var parsedStart))
        {
            start = parsedStart;
            return true;
        }

        var startQuery = ExtractQueryParam(url, "start");
        if (!string.IsNullOrWhiteSpace(startQuery) && int.TryParse(startQuery, out parsedStart))
        {
            start = parsedStart;
            return true;
        }

        start = 0;
        return true;
    }

    static bool TryExtractIdAndStart(string value, out string id, out int start)
    {
        id = string.Empty;
        start = 0;

        var parts = value.Split('.');
        if (parts.Length < 2)
            return false;

        if (string.IsNullOrWhiteSpace(parts[0]) || !int.TryParse(parts[1], out start))
            return false;

        id = parts[0];
        return true;
    }

    static bool TryExtractStartOnly(string url, out int start)
    {
        start = 0;

        var startQuery = ExtractQueryParam(url, "start");
        if (!string.IsNullOrWhiteSpace(startQuery) && int.TryParse(startQuery, out var parsedStart))
        {
            start = parsedStart;
            return true;
        }

        var match = Regex.Match(
            url,
            @"(?:^|[/?&;])start(?:=|,)(?<start>\d+)",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups["start"].Value, out parsedStart))
            return false;

        start = parsedStart;
        return true;
    }

    static IDocument ParseDocument(string html)
    {
        var parser = new HtmlParser();
        return parser.ParseDocument(html);
    }

    static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    static string CleanCategoryText(string? value)
    {
        var text = NormalizeWhitespace(value);
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        text = text
            .Replace("Unread Posts", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Unread", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Posts", string.Empty, StringComparison.OrdinalIgnoreCase);

        return NormalizeWhitespace(text);
    }
}
