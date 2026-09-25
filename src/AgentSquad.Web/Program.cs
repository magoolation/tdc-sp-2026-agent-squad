using AgentSquad.Agents.DependencyInjection;
using AgentSquad.Core.Abstractions;
using AgentSquad.ServiceDefaults;
using AgentSquad.Web.Components;
using AgentSquad.Web.Runs;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddAgentSquad(builder.Configuration);

// The gateway is shared between the orchestrator, which awaits on it from a background
// thread, and the Blazor circuit that answers it.
builder.Services.AddSingleton<WebApprovalGateway>();
builder.Services.AddSingleton<IApprovalGateway>(sp => sp.GetRequiredService<WebApprovalGateway>());
builder.Services.AddSingleton<RunCoordinator>();

builder.Services.AddAntiforgery();

// Content Security Policy is declared here rather than in a meta tag so it also covers
// the SignalR negotiate response and any non-HTML endpoint (SEC-009).
builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    // 'unsafe-inline' for styles only: Blazor's own component styles are inlined, while
    // scripts stay restricted to same-origin files (SEC-009).
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "connect-src 'self' ws: wss:; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'";

    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.Append("Referrer-Policy", "no-referrer");

    await next();
});

app.UseAntiforgery();
app.MapStaticAssets();
app.MapDefaultEndpoints();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
