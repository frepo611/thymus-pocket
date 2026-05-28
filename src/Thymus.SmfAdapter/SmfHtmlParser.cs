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

    private static IDocument ParseDocument(string html)
    {
        var parser = new HtmlParser();
        return parser.ParseDocument(html);
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
