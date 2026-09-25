// =============================================================================
// Aspire orchestration for the Agent Squad dashboard.
//
// The reason this exists for a single web project is the Aspire dashboard: it is
// an OpenTelemetry viewer, and it has a dedicated GenAI visualizer. Running the
// factory under it means every agent call — invoke_agent, chat, execute_tool,
// and the workflow spans — is inspectable live, with token counts and latency,
// while the audience watches.
//
//   dotnet run --project src/AgentSquad.AppHost
//
// The dashboard URL, with its one-time login token, is printed on startup.
// =============================================================================

using Microsoft.Extensions.Hosting;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Configuration is forwarded rather than duplicated. The Foundry endpoint and the
// GitHub coordinates live in user secrets on the operator's machine (SEC-001), and
// the AppHost passes them through so the web project needs no secrets of its own.
IResourceBuilder<ProjectResource> web = builder
    .AddProject<Projects.AgentSquad_Web>("dashboard")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithEnvironment("Foundry__ProjectEndpoint", builder.Configuration["Foundry:ProjectEndpoint"] ?? string.Empty)
    .WithEnvironment("GitHub__Owner", builder.Configuration["GitHub:Owner"] ?? string.Empty)
    .WithEnvironment("GitHub__Repository", builder.Configuration["GitHub:Repository"] ?? string.Empty)

    // Captures prompts and completions in traces. On by default here and only here:
    // the whole point of running under Aspire is to see what the agents actually said,
    // and the dashboard is a local, ephemeral viewer. Production keeps this off (AI-003).
    .WithEnvironment("Foundry__CaptureMessageContent", "true")
    .WithEnvironment("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT", "true");

if (builder.Environment.IsDevelopment())
{
    web.WithUrlForEndpoint("https", url => url.DisplayText = "Agent Squad");
}

builder.Build().Run();
