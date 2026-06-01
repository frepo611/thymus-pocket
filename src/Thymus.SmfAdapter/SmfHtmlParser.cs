using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Thymus.SmfAdapter.Models;

namespace Thymus.SmfAdapter;

public static class SmfHtmlParser
{
    public static List<ThreadSummary> ParseThreadList(string html)
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

    public static List<PostContent> ParseTopic(string html)
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
