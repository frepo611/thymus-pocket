global using BoardDto = Thymus.Contracts.BoardDto;
global using ThreadSummaryDto = Thymus.Contracts.ThreadSummaryDto;
global using PostDto = Thymus.Contracts.PostDto;
global using ThreadDetailsDto = Thymus.Contracts.ThreadDetailsDto;
global using TopicsPageDto = Thymus.Contracts.TopicsPageDto;
global using PostsPageDto = Thymus.Contracts.PostsPageDto;
namespace Thymus.Bff.Contracts;

// BFF-internal request types (not shared with clients)
public sealed record LoginRequest(string Username, string Password);
public sealed record ReplyRequestDto(string Subject, string Message);
public sealed record ThreadReplyRequest(string Url, string Subject, string Message);

