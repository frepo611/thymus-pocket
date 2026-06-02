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
                <td class='subject'><span id='msg_123'><a href='https://forum/index.php?topic=123.0'>Hej varlden</a></span></td>
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

      [Fact]
      public void GetNextPostStart_FindsNextOffsetFromTopicLinks()
      {
        const string html = """
          <div class='pagelinks'>
            <a href='index.php?topic=425.0'>1</a>
            <a href='index.php?topic=425.20'>2</a>
            <a href='index.php?topic=425.40'>3</a>
            <a href='index.php?topic=462.20'>Other topic</a>
          </div>
          """;

        Assert.Equal(20, SmfHtmlParser.GetNextPostStart(html, "425", 0));
        Assert.Equal(40, SmfHtmlParser.GetNextPostStart(html, "425", 20));
        Assert.Null(SmfHtmlParser.GetNextPostStart(html, "425", 40));
      }

      [Fact]
      public void GetNextPostStart_FindsNextOffsetFromStartOnlyPaginationLinks()
      {
        const string html = """
          <div class='pagelinks'>
            <a href='index.php?start=0'>1</a>
            <a href='index.php?start=20'>2</a>
            <a href='index.php?board=6.30'>Board link</a>
          </div>
          """;

        Assert.Equal(20, SmfHtmlParser.GetNextPostStart(html, "425", 0));
      }

      [Fact]
      public void GetNextPostStart_IgnoresActionLinksWithTopicFractionalOffsets()
      {
        const string html = """
          <div>
            <a href='index.php?action=reporttm;topic=425.1;msg=16332'>Report</a>
            <a href='index.php?action=post;quote=16331;topic=425.0;last_msg=163091'>Quote</a>
            <a href='index.php?topic=425.20'>2</a>
            <a href='index.php?topic=425.40'>3</a>
          </div>
          """;

        Assert.Equal(20, SmfHtmlParser.GetNextPostStart(html, "425", 0));
      }

      [Fact]
      public void ExtractTopicPageStarts_ReturnsOrderedUniqueStarts()
      {
        const string html = """
          <div class='pagelinks'>
            <a href='index.php?topic=425.40'>3</a>
            <a href='index.php?topic=425.20'>2</a>
            <a href='index.php?topic=425.20'>2-dup</a>
            <a href='index.php?action=reporttm;topic=425.1;msg=16332'>Report</a>
          </div>
          """;

        var starts = SmfHtmlParser.ExtractTopicPageStarts(html, "425");
        Assert.Equal(new[] { 0, 20, 40 }, starts);
      }

      [Fact]
      public void GetPreviousPostStart_FindsPreviousOffset()
      {
        const string html = """
          <div class='pagelinks'>
            <a href='index.php?topic=425.0'>1</a>
            <a href='index.php?topic=425.20'>2</a>
            <a href='index.php?topic=425.40'>3</a>
          </div>
          """;

        Assert.Equal(20, SmfHtmlParser.GetPreviousPostStart(html, "425", 40));
        Assert.Equal(0, SmfHtmlParser.GetPreviousPostStart(html, "425", 20));
        Assert.Null(SmfHtmlParser.GetPreviousPostStart(html, "425", 0));
      }

      [Fact]
      public void GetLatestPostStart_FindsHighestOffset()
      {
        const string html = """
          <div class='pagelinks'>
            <a href='index.php?topic=425.0'>1</a>
            <a href='index.php?topic=425.20'>2</a>
            <a href='index.php?topic=425.300'>16</a>
          </div>
          """;

        Assert.Equal(300, SmfHtmlParser.GetLatestPostStart(html, "425"));
      }

      [Fact]
      public void GetNextBoardStart_FindsNextOffsetFromBoardLinks()
      {
        const string html = """
          <div class='pagelinks'>
            <a href='index.php?board=6.0'>1</a>
            <a href='index.php?board=6.30'>2</a>
            <a href='index.php?board=6.60'>3</a>
            <a href='index.php?board=7.30'>Other board</a>
          </div>
          """;

        Assert.Equal(30, SmfHtmlParser.GetNextBoardStart(html, "6", 0));
        Assert.Equal(60, SmfHtmlParser.GetNextBoardStart(html, "6", 30));
        Assert.Null(SmfHtmlParser.GetNextBoardStart(html, "6", 60));
      }

      [Fact]
      public void GetNextBoardStart_FindsNextOffsetFromStartOnlyPaginationLinks()
      {
        const string html = """
          <div class='pagelinks'>
            <a href='index.php?start=0'>1</a>
            <a href='index.php?start=30'>2</a>
            <a href='index.php?topic=704.20'>Topic link</a>
          </div>
          """;

        Assert.Equal(30, SmfHtmlParser.GetNextBoardStart(html, "6", 0));
      }
}
