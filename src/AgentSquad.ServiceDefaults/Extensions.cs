using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AgentSquad.ServiceDefaults;

/// <summary>
/// Cross-cutting configuration every hosted service in the solution shares.
/// </summary>
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    /// <summary>
    /// Adds telemetry, health checks, service discovery and HTTP resilience.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Retry, circuit breaker and timeout by default rather than per call site,
            // so no HttpClient in the solution can be created without them (ENG-047).
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// Configures logging, metrics and tracing.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddRuntimeInstrumentation();

                // The Agent Framework's meter name is always identical to its activity
                // source name, so the same string has to be registered in both places.
                metrics.AddMeter(AgentTelemetry.AgentSourceName);
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation(o =>
                           o.Filter = context => !IsHealthEndpoint(context.Request.Path))
                       .AddHttpClientInstrumentation();

                // Agent spans: invoke_agent, chat, execute_tool.
                tracing.AddSource(AgentTelemetry.AgentSourceName);

                // Workflow spans. Registered explicitly because the wildcard form used in
                // some samples ("*Microsoft.Agents.AI") is a suffix match and does not
                // cover "Microsoft.Agents.AI.Workflows".
                tracing.AddSource(AgentTelemetry.WorkflowSourceName);

                // Spans emitted by the Azure SDK on the way to Foundry.
                tracing.AddSource("Azure.AI.Projects.*");
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        bool useOtlpExporter =
            !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>
    /// Adds a liveness check every service exposes.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <returns>The builder, for chaining.</returns>
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHealthChecks()
               .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Maps the health endpoints, in development only.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The application, for chaining.</returns>
    /// <remarks>
    /// Health endpoints are not exposed outside development: they leak the existence and
    /// liveness of internal dependencies, which is information an attacker can use (SEC-012).
    /// </remarks>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(HealthEndpointPath);

            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("live"),
            });
        }

        return app;
    }

    private static bool IsHealthEndpoint(PathString path) =>
        path.StartsWithSegments(HealthEndpointPath) || path.StartsWithSegments(AlivenessEndpointPath);
}

/// <summary>
/// The telemetry source names the solution registers.
/// </summary>
/// <remarks>
/// Declared here rather than referenced from the agents assembly so that
/// <c>ServiceDefaults</c> stays free of a dependency on the agent stack.
/// </remarks>
public static class AgentTelemetry
{
    /// <summary>Activity source and meter name used by the factory's agents.</summary>
    public const string AgentSourceName = "AgentSquad.Agents";

    /// <summary>Activity source emitted by <c>Microsoft.Agents.AI.Workflows</c>.</summary>
    public const string WorkflowSourceName = "Microsoft.Agents.AI.Workflows";
}
