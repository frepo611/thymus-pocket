namespace Thymus.Contracts;

public sealed record BoardDto(string Id, string Name, string Url, string Category);

public sealed record ThreadSummaryDto(
    string Id,
    string Title,
    string Board,
    string Url,
    string? LastPostBy,
    string? LastPostAt);

public sealed record PostDto(
    int MessageId,
    string Author,
    string Body,
    DateTimeOffset? PostedAt);

public sealed record ThreadDetailsDto(
    string Id,
    string Title,
    string Url,
    IReadOnlyList<PostDto> Posts);

public sealed record TopicsPageDto(
    IReadOnlyList<ThreadSummaryDto> Items,
    int? NextStart);

public sealed record PostsPageDto(
    string Title,
    IReadOnlyList<PostDto> Posts,
    int? NextStart);
