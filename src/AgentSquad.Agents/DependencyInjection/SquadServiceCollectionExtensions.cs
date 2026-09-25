using AgentSquad.Agents.Foundry;
using AgentSquad.Agents.Orchestration;
using AgentSquad.Agents.Pipeline;
using AgentSquad.Agents.Runs;
using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Configuration;
using AgentSquad.Core.Events;
using AgentSquad.Tools.Git;
using AgentSquad.Tools.GitHub;
using AgentSquad.Tools.Prerequisites;
using AgentSquad.Tools.Processes;
using AgentSquad.Tools.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentSquad.Agents.DependencyInjection;

/// <summary>
/// Registers everything the software factory needs.
/// </summary>
public static class SquadServiceCollectionExtensions
{
    /// <summary>
    /// Adds the factory's options, tools, agents and orchestration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration root.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddAgentSquad(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ValidateOnStart so a misconfigured endpoint or repository fails at startup with a
        // clear message, rather than twenty minutes into a run (ENG-007).
        services.AddOptions<SquadOptions>()
                .Bind(configuration.GetSection(SquadOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

        services.AddOptions<FoundryOptions>()
                .Bind(configuration.GetSection(FoundryOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

        services.AddOptions<GitHubOptions>()
                .Bind(configuration.GetSection(GitHubOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

        services.AddOptions<CopilotOptions>()
                .Bind(configuration.GetSection(CopilotOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

        services.TryAddSingletonTimeProvider();

        // ---- Deterministic tools -------------------------------------------------------
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IGitClient, GitClient>();
        services.AddSingleton<GitWorktreeManager>();
        services.AddSingleton<IWorktreeManager>(sp => sp.GetRequiredService<GitWorktreeManager>());
        services.AddSingleton<IIssueTracker, GitHubCliIssueTracker>();
        services.AddSingleton<IValidationGate, DotNetValidationGate>();
        services.AddSingleton<PrerequisiteChecker>();

        // ---- Agents --------------------------------------------------------------------
        services.AddSingleton<FoundryAgentFactory>();
        services.AddSingleton<FoundrySmokeTest>();
        services.AddSingleton<SquadAgentSet>();
        services.AddSingleton<ICodingAgentFactory, Implementation.CopilotCodingAgentFactory>();
        services.AddSingleton<Implementation.CopilotSmokeTest>();

        // ---- Run stream ----------------------------------------------------------------
        services.AddSingleton<RunEventBus>();
        services.AddSingleton<IRunEventStream>(sp => sp.GetRequiredService<RunEventBus>());

        // ---- Orchestration -------------------------------------------------------------
        services.AddSingleton<SquadOrchestrator>();

        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        // TimeProvider everywhere rather than DateTime.UtcNow, so every timestamp in the
        // factory can be made deterministic in a test (TST-004).
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
