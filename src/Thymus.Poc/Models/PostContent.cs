namespace Thymus.Poc.Models;

public record PostContent(
    int MessageId,
    string Author,
    string Body,
    DateTimeOffset? PostedAt);
