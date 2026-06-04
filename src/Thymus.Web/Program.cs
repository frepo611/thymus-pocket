using Thymus.Web.Components;
using Thymus.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<BffApiClient>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    var baseUrl = configuration["Bff:BaseUrl"] ?? "https://localhost:5001";
    var internalApiSecret = configuration["Bff:InternalApiSharedSecret"]
        ?? throw new InvalidOperationException("Bff:InternalApiSharedSecret is required.");
    return new BffApiClient(baseUrl, internalApiSecret, httpContextAccessor);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
