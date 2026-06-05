using System.Net.Http.Json;
using Thymus.Contracts;

namespace Thymus.Web.Client.Services;

public sealed class BffApiClient(HttpClient http)
{
    public async Task<bool> LoginAsync(string username, string password)
    {
        using var response = await http.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<BoardDto>?> GetBoardsAsync()
    {
        using var response = await http.GetAsync("/api/boards");
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<BoardDto>>();
    }

    public async Task<IReadOnlyList<ThreadSummaryDto>?> GetThreadsAsync()
    {
        using var response = await http.GetAsync("/api/threads");
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<ThreadSummaryDto>>();
    }

    public async Task<TopicsPageDto?> GetTopicsPageAsync(string boardId, int start)
    {
        using var response = await http.GetAsync(
            $"/api/topics?boardId={Uri.EscapeDataString(boardId)}&start={start}");
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<TopicsPageDto>();
    }

    public async Task<PostsPageDto?> GetThreadPageAsync(string topicUrl, int start, bool newestFirst = true)
    {
        using var response = await http.GetAsync(
            $"/api/thread?url={Uri.EscapeDataString(topicUrl)}&start={start}&newestFirst={newestFirst.ToString().ToLowerInvariant()}");
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<PostsPageDto>();
    }

    public async Task<bool> PostReplyAsync(string topicUrl, string subject, string message)
    {
        using var response = await http.PostAsJsonAsync(
            "/api/thread/reply",
            new ReplyRequest(topicUrl, subject, message));
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> LogoutAsync()
    {
        using var response = await http.PostAsync("/api/auth/logout", content: null);
        return response.IsSuccessStatusCode;
    }

    private sealed record LoginRequest(string Username, string Password);
    private sealed record ReplyRequest(string Url, string Subject, string Message);
}
