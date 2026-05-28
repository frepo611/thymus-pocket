using Thymus.SmfAdapter;
using Xunit;

namespace Thymus.SmfAdapter.Tests;

public sealed class SmfHtmlParserTests
{
    [Fact]
    public void ParseThreadList_ParsesClassicTableRows()
    {
        const string html = """
            <table>
              <tr>
                <td class='subject'><a href='https://forum/index.php?topic=123.0'>Hej varlden</a></td>
                <td class='board'><a href='https://forum/index.php?board=5.0'>Allmant</a></td>
                <td class='lastpost'><a href='https://forum/index.php?action=profile;u=1'>Ladde</a> Idag 10:00</td>
              </tr>
            </table>
            """;

        var results = SmfHtmlParser.ParseThreadList(html);

        Assert.Single(results);
        Assert.Equal("Hej varlden", results[0].Title);
        Assert.Equal("Allmant", results[0].Board);
        Assert.Equal("https://forum/index.php?topic=123.0", results[0].Url);
        Assert.Equal("Ladde", results[0].LastPostBy);
    }

    [Fact]
    public void ParseBoardUrls_FiltersOutUnreadActionLinks()
    {
        const string html = """
            <a href='https://forum/index.php?board=2.0'>B2</a>
            <a href='https://forum/index.php?action=unread;board=2.0'>Unread</a>
            <a href='https://forum/index.php?board=3.0'>B3</a>
            """;

        var results = SmfHtmlParser.ParseBoardUrls(html);

        Assert.Equal(2, results.Count);
        Assert.Contains("https://forum/index.php?board=2.0", results);
        Assert.Contains("https://forum/index.php?board=3.0", results);
    }

    [Fact]
    public void ParseTopic_ParsesPostCards()
    {
        const string html = """
            <div id='forumposts'>
              <div class='windowbg'>
                <div class='poster'><h4><a>Ladde</a></h4></div>
                <div class='post'>
                  <div class='inner' data-msgid='42'>  Forsta   inlagget </div>
                </div>
                <div class='postinfo'><a rel='nofollow'>2026-05-28 12:34 +00:00</a></div>
              </div>
            </div>
            """;

        var posts = SmfHtmlParser.ParseTopic(html);

        Assert.Single(posts);
        Assert.Equal(42, posts[0].MessageId);
        Assert.Equal("Ladde", posts[0].Author);
        Assert.Equal("Forsta inlagget", posts[0].Body);
        Assert.True(posts[0].PostedAt.HasValue);
    }
}
