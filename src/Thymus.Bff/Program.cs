using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Thymus.Bff;
using Thymus.Bff.Contracts;
using Thymus.Bff.Endpoints;
using Thymus.SmfAdapter;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddRateLimiter(options =>
{
    options.AddSlidingWindowLimiter("login", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(15);
        opt.SegmentsPerWindow = 1;
        opt.PermitLimit = 5;
        opt.QueueLimit = 2;
    });

    options.AddSlidingWindowLimiter("read", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.SegmentsPerWindow = 1;
        opt.PermitLimit = 100;
        opt.QueueLimit = 10;
    });

    options.AddSlidingWindowLimiter("write", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.SegmentsPerWindow = 1;
        opt.PermitLimit = 20;
        opt.QueueLimit = 5;
    });
});

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    var ex = feature?.Error;

    context.Response.ContentType = "application/json";
    context.Response.StatusCode = ex switch
    {
        TaskCanceledException or OperationCanceledException => 504,
        UnauthorizedAccessException => 401,
        ArgumentException => 400,
        _ => 500,
    };

    var message = ex switch
    {
        TaskCanceledException or OperationCanceledException => "Forumet svarade inte i tid, försök igen.",
        UnauthorizedAccessException => "Åtkomst nekad.",
        ArgumentException e => e.Message,
        _ => "Ett oväntat fel inträffade.",
    };

    var detail = app.Environment.IsDevelopment() ? ex?.ToString() : null;
    await context.Response.WriteAsJsonAsync(new { error = ex?.GetType().Name, message, detail });
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseRateLimiter();

var smfBaseUrl = builder.Configuration["Smf:BaseUrl"]
    ?? throw new InvalidOperationException("Smf:BaseUrl is required.");

var sessionStoreDirectoryRaw = builder.Configuration["Session:StoreDirectory"] ?? ".sessions";
var sessionCookieName = builder.Configuration["Session:CookieName"] ?? "thymus_session";
var trustedCallerSecret = builder.Configuration["InternalApi:SharedSecret"]
    ?? throw new InvalidOperationException("InternalApi:SharedSecret is required.");
var sessionStoreDirectory = Path.IsPathRooted(sessionStoreDirectoryRaw)
    ? sessionStoreDirectoryRaw
    : Path.Combine(app.Environment.ContentRootPath, sessionStoreDirectoryRaw);
Directory.CreateDirectory(sessionStoreDirectory);

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    if (!context.Request.Headers.TryGetValue("X-Thymus-Internal-Secret", out var headerValue))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { message = "Forbidden." });
        return;
    }

    var providedSecret = headerValue.ToString();
    if (string.IsNullOrWhiteSpace(providedSecret) || !FixedTimeEquals(providedSecret, trustedCallerSecret))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { message = "Forbidden." });
        return;
    }

    await next();
});

var sessions = new ConcurrentDictionary<string, SessionState>(StringComparer.Ordinal);

var bffContext = new BffContext
{
    SmfBaseUrl = smfBaseUrl,
    Sessions = sessions,
    SessionCookieName = sessionCookieName,
    SessionStoreDirectory = sessionStoreDirectory,
};

app.MapAuthEndpoints(bffContext);
app.MapBoardsEndpoints(bffContext);
app.MapTopicsEndpoints(bffContext);
app.MapThreadEndpoints(bffContext);
app.MapThreadsEndpoints(bffContext);
app.MapDebugEndpoints(bffContext);

app.Run();

static bool FixedTimeEquals(string providedValue, string expectedValue)
{
    var providedBytes = Encoding.UTF8.GetBytes(providedValue);
    var expectedBytes = Encoding.UTF8.GetBytes(expectedValue);
    return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
}












