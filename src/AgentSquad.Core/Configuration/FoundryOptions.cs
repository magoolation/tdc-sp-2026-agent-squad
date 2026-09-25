using System.ComponentModel.DataAnnotations;

namespace AgentSquad.Core.Configuration;

/// <summary>
/// How the orchestrator reaches Microsoft Foundry.
/// </summary>
/// <remarks>
/// There is no API key here, and that is deliberate: authentication is Microsoft Entra ID
/// through <c>DefaultAzureCredential</c> (SEC-002), and the provisioned account sets
/// <c>disableLocalAuth</c>, so a key would not work even if one were configured.
/// </remarks>
public sealed class FoundryOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Foundry";

    /// <summary>
    /// Gets or sets the Foundry project endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shaped <c>https://&lt;account&gt;.services.ai.azure.com/api/projects/&lt;project&gt;</c>.
    /// </para>
    /// <para>
    /// Deliberately <b>not</b> marked <c>[Required]</c>. The <c>doctor</c> command exists to
    /// diagnose incomplete configuration, and a data-annotation failure here would make it
    /// throw on startup instead of reporting the very problem it was asked to find.
    /// <c>PrerequisiteChecker</c> validates it and explains how to fix it; the agent factory
    /// fails fast with an actionable message if a run starts without it.
    /// </para>
    /// </remarks>
    public string ProjectEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the deployment used for reasoning-heavy work: requirements and planning.
    /// </summary>
    [Required]
    public string PlanningModel { get; set; } = "gpt-5.5";

    /// <summary>
    /// Gets or sets the deployment used for high-volume structured work: critique, review, rendering.
    /// </summary>
    [Required]
    public string WorkhorseModel { get; set; } = "gpt-5.4-mini";

    /// <summary>
    /// Gets or sets the deployment used for reviewing code diffs.
    /// </summary>
    [Required]
    public string ReviewModel { get; set; } = "gpt-5.3-codex";

    /// <summary>
    /// Gets or sets the reasoning effort requested from models that support it.
    /// </summary>
    /// <remarks>Valid values: <c>none</c>, <c>minimal</c>, <c>low</c>, <c>medium</c>, <c>high</c>, <c>xhigh</c>.</remarks>
    public string ReasoningEffort { get; set; } = "medium";

    /// <summary>
    /// Gets or sets the sampling temperature, or <see langword="null"/> to let the model decide.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Null by default, and that is deliberate.</b> Reasoning models reject the parameter
    /// outright — <c>gpt-5.5</c> answers
    /// <c>HTTP 400: Unsupported parameter: 'temperature' is not supported with this model</c>,
    /// while <c>gpt-5.4-mini</c> accepts it. Sending a fixed temperature therefore makes the
    /// model choice and the sampling setting silently coupled, and swapping in a stronger
    /// model breaks the pipeline at the first call.
    /// </para>
    /// <para>
    /// Determinism does not depend on this anyway: the structured-output schema is what
    /// constrains these agents, and the deterministic gate is what decides pass or fail
    /// (AI-008). Set it only when you know the deployment supports it.
    /// </para>
    /// </remarks>
    [Range(0.0, 2.0)]
    public double? Temperature { get; set; }

    /// <summary>
    /// Gets or sets how long a single call to a model may take.
    /// </summary>
    /// <remarks>
    /// The SDK default is 100 seconds, which is fine for chat and wrong for this workload:
    /// the architect is a reasoning model producing a whole delivery plan as structured
    /// output, and it went past 100 seconds on a 22-requirement plan. The retry policy then
    /// spent four attempts hitting the same wall and failed the run after nine minutes, with
    /// a message about network timeouts that says nothing about the real cause.
    /// <para>
    /// Five minutes, not more: this is a per-attempt budget and the retry policy makes four
    /// attempts, so the value also decides how long a genuinely stuck call can hold the run
    /// silent. Five gives three times the headroom over the call that failed, and still
    /// bounds the worst case at twenty minutes.
    /// </para>
    /// </remarks>
    [Range(typeof(TimeSpan), "00:00:30", "00:30:00")]
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets a value indicating whether prompts and completions are captured in traces.
    /// </summary>
    /// <remarks>
    /// Off by default (SEC-002, AI-003). Turn it on only for a demo with synthetic data:
    /// it writes full prompt content into the telemetry pipeline.
    /// </remarks>
    public bool CaptureMessageContent { get; set; }
}

