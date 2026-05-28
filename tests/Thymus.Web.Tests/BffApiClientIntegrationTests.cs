using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Thymus.Web.Contracts;
using Thymus.Web.Services;
using Xunit;

namespace Thymus.Web.Tests;

public sealed class BffApiClientIntegrationTests
{
    [Fact]
    public async Task LoginAsync_AllowsAuthenticatedThreadsRequest()
    {
        await using var host = await TestBffHost.StartAsync(app =>
        {
            app.MapPost("/api/auth/login", (HttpContext context) =>
            {
                context.Response.Cookies.Append("thymus_session", "session-123", new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                });

                return Results.Ok();
            });

            app.MapGet("/api/threads", (HttpContext context) =>
            {
                if (!context.Request.Cookies.ContainsKey("thymus_session"))
                    return Results.Unauthorized();

                return Results.Json(new[]
                {
                    new ThreadSummaryDto(
                        Id: "42",
                        Title: "Testtrad",
                        Board: "Allmant",
                        Url: "https://forum.local/index.php?topic=42.0",
                        LastPostBy: "alice",
                        LastPostAt: "2026-05-28 10:00")
                });
            });
        });

        using var client = new BffApiClient(host.BaseUrl);

        var loginOk = await client.LoginAsync("alice", "secret");
        var threads = await client.GetThreadsAsync();

        Assert.True(loginOk);
        Assert.Single(threads);
        Assert.Equal("42", threads[0].Id);
    }

    [Fact]
    public async Task GetThreadsAsync_ThrowsUnauthorized_WhenMissingSession()
    {
        await using var host = await TestBffHost.StartAsync(app =>
        {
            app.MapGet("/api/threads", () => Results.Unauthorized());
        });

        using var client = new BffApiClient(host.BaseUrl);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.GetThreadsAsync());
    }

    [Fact]
    public async Task LogoutAsync_ClearsClientSessionCookie()
    {
        await using var host = await TestBffHost.StartAsync(app =>
        {
            app.MapPost("/api/auth/login", (HttpContext context) =>
            {
                context.Response.Cookies.Append("thymus_session", "session-123", new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                });

                return Results.Ok();
            });

            app.MapPost("/api/auth/logout", () => Results.Ok());

            app.MapGet("/api/threads", (HttpContext context) =>
            {
                if (!context.Request.Cookies.ContainsKey("thymus_session"))
                    return Results.Unauthorized();

                return Results.Json(Array.Empty<ThreadSummaryDto>());
            });
        });

        using var client = new BffApiClient(host.BaseUrl);

        var loginOk = await client.LoginAsync("alice", "secret");
        await client.LogoutAsync();

        Assert.True(loginOk);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.GetThreadsAsync());
    }

    [Fact]
    public async Task PostReplyAsync_ReturnsFalse_WhenThreadIsMissing()
    {
        await using var host = await TestBffHost.StartAsync(app =>
        {
            app.MapPost("/api/threads/{id}/replies", () => Results.NotFound());
        });

        using var client = new BffApiClient(host.BaseUrl);

        var result = await client.PostReplyAsync("999", "Re: Missing", "Reply text");

        Assert.False(result);
    }

    private sealed class TestBffHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private TestBffHost(WebApplication app, string baseUrl)
        {
            _app = app;
            BaseUrl = baseUrl;
        }

        public string BaseUrl { get; }

        public static async Task<TestBffHost> StartAsync(Action<WebApplication> configure)
        {
            var port = GetFreePort();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

            var app = builder.Build();
            configure(app);
            await app.StartAsync();

            return new TestBffHost(app, $"http://127.0.0.1:{port}");
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
