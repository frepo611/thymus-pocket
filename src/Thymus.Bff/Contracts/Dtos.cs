namespace Thymus.Bff.Contracts;

public sealed record LoginRequest(string Username, string Password);

public sealed record ReplyRequestDto(string Subject, string Message);

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
