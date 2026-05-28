namespace Thymus.SmfAdapter.Models;

public sealed record PostContent(
    int MessageId,
    string Author,
    string Body,
    DateTimeOffset? PostedAt);
