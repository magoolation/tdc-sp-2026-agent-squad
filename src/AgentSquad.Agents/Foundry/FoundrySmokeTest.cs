using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using AgentSquad.Core.Configuration;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace AgentSquad.Agents.Foundry;

/// <summary>The outcome of probing one model deployment.</summary>
/// <param name="Deployment">The deployment name that was probed.</param>
/// <param name="Role">What the deployment is used for.</param>
/// <param name="Reachable">Whether a real call succeeded.</param>
/// <param name="Latency">Round-trip time of the call.</param>
/// <param name="Detail">The model's answer, or the failure message.</param>
public sealed record ModelProbe(string Deployment, string Role, bool Reachable, TimeSpan Latency, string Detail);

/// <summary>
/// Proves, with a real call, that the whole path to Microsoft Foundry works.
/// </summary>
/// <remarks>
/// <para>
/// Checking that an endpoint is configured proves nothing. This exercises the entire chain
/// a run depends on — Entra ID token acquisition, the RBAC assignment on the account, the
/// model deployment's quota, and the Agent Framework's structured-output path — before any
/// work is planned, and certainly before anyone stands in front of an audience.
/// </para>
/// <para>
/// It asks for structured output rather than free text on purpose. Structured output is
/// what the orchestrator actually relies on, and it is the part most likely to fail on a
/// deployment that is otherwise reachable.
/// </para>
/// </remarks>
public sealed class FoundrySmokeTest(FoundryAgentFactory factory, IOptions<FoundryOptions> options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly FoundryAgentFactory _factory = factory;
    private readonly FoundryOptions _options = options.Value;

    /// <summary>
    /// Probes every deployment the factory will use.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One result per deployment.</returns>
    public async Task<IReadOnlyList<ModelProbe>> ProbeAllAsync(CancellationToken cancellationToken)
    {
        (ModelTier Tier, string Role)[] tiers =
        [
            (ModelTier.Planning, "requisitos e planejamento"),
            (ModelTier.Workhorse, "crítica e tarefas estruturadas"),
            (ModelTier.Review, "revisão de código"),
        ];

        var results = new List<ModelProbe>(tiers.Length);
        var probed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach ((ModelTier tier, string role) in tiers)
        {
            string deployment = _factory.ResolveModel(tier);

            // Two tiers may legitimately point at the same deployment; probe it once.
            if (!probed.Add(deployment))
            {
                continue;
            }

            results.Add(await ProbeAsync(tier, deployment, role, cancellationToken));
        }

        return results;
    }

    private async Task<ModelProbe> ProbeAsync(
        ModelTier tier,
        string deployment,
        string role,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            AIAgent agent = _factory.Create(
                $"smoke-{tier.ToString().ToLowerInvariant()}",
                "You verify connectivity. Answer only with the requested JSON, nothing else.",
                tier,
                "Connectivity probe.");

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(60));

            AgentResponse<SmokeAnswer> response = await agent.RunAsync<SmokeAnswer>(
                "Responda com ok=true e uma saudação curta em português para o TDC São Paulo 2026.",
                session: null,
                serializerOptions: Json,
                options: null,
                cancellationToken: budget.Token);

            stopwatch.Stop();

            SmokeAnswer answer = response.Result;

            return new ModelProbe(
                deployment,
                role,
                answer.Ok,
                stopwatch.Elapsed,
                $"{answer.Greeting} ({response.Usage?.TotalTokenCount ?? 0} tokens)");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new ModelProbe(deployment, role, false, stopwatch.Elapsed, "A chamada excedeu 60 segundos.");
        }
#pragma warning disable CA1031 // A probe reports every failure as a result; it must never throw.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            stopwatch.Stop();
            return new ModelProbe(deployment, role, false, stopwatch.Elapsed, Explain(ex));
        }
    }

    /// <summary>
    /// Turns a raw SDK exception into something an operator can act on.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>A message naming the likely cause and the fix.</returns>
    private string Explain(Exception exception)
    {
        string message = exception.Message;

        if (message.Contains("DeploymentNotFound", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            return "Deployment não encontrado no projeto. Confira os nomes em Foundry:* contra " +
                   "'az cognitiveservices account deployment list'.";
        }

        if (message.Contains("PermissionDenied", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("403", StringComparison.Ordinal))
        {
            return "Acesso negado. A identidade precisa do papel Foundry User no escopo da conta — " +
                   "a atribuição pode levar alguns minutos para propagar.";
        }

        if (message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("401", StringComparison.Ordinal) ||
            message.Contains("credential", StringComparison.OrdinalIgnoreCase))
        {
            return "Falha ao obter token do Entra ID. Rode 'az login' e confirme a subscription correta.";
        }

        if (message.Contains("429", StringComparison.Ordinal) ||
            message.Contains("rate", StringComparison.OrdinalIgnoreCase))
        {
            return $"Rate limit atingido. Aumente a capacidade do deployment (atualmente configurado para " +
                   $"{_options.PlanningModel}/{_options.WorkhorseModel}/{_options.ReviewModel}).";
        }

        return message.Length <= 200 ? message : message[..200] + "…";
    }

    private sealed record SmokeAnswer(
        [property: Description("Sempre true quando a chamada funcionou.")] bool Ok,
        [property: Description("Uma saudação curta em português.")] string Greeting);
}
