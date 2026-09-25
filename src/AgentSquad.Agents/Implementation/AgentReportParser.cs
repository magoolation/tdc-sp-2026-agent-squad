using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AgentSquad.Core.Implementation;

namespace AgentSquad.Agents.Implementation;

/// <summary>
/// Extracts the structured report a coding agent prints at the end of its run.
/// </summary>
/// <remarks>
/// <para>
/// Parsing is forgiving by design. A model asked to print a fenced JSON block will
/// occasionally add a stray line, use a different fence language tag, or emit the block
/// twice. None of those is worth failing a run that produced good code — the authoritative
/// pass or fail comes from the validation gate, not from this report (AI-008).
/// </para>
/// <para>
/// What parsing must not do is invent a success. When no report can be read, the outcome is
/// <see cref="ImplementationStatus.Partial"/> with an explicit note, never
/// <see cref="ImplementationStatus.Completed"/>.
/// </para>
/// </remarks>
public static partial class AgentReportParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>
    /// Parses the agent's output.
    /// </summary>
    /// <param name="output">Everything the agent printed.</param>
    /// <param name="issueNumber">The issue being implemented, used when the report omits it.</param>
    /// <returns>The parsed report, or a conservative fallback when none could be read.</returns>
    public static AgentReport Parse(string output, int issueNumber)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new AgentReport(
                issueNumber,
                ImplementationStatus.Failed,
                "The agent produced no output.",
                [], [], [], [],
                "No output was captured from the agent.");
        }

        foreach (string candidate in ExtractCandidates(output))
        {
            if (TryDeserialize(candidate, issueNumber, out AgentReport? report))
            {
                return report;
            }
        }

        // The agent worked but did not emit a parseable report. Its work still has to face
        // the validation gate, so the honest status is "partial", not "completed".
        return new AgentReport(
            issueNumber,
            ImplementationStatus.Partial,
            Summarize(output),
            [], [], [],
            ["The agent did not emit a parseable AGENT_REPORT block; the summary above is the tail of its output."],
            null);
    }

    private static IEnumerable<string> ExtractCandidates(string output)
    {
        // Preferred: the exact contract, ```json AGENT_REPORT.
        foreach (Match match in TaggedBlockRegex().Matches(output).Cast<Match>().Reverse())
        {
            yield return match.Groups["json"].Value;
        }

        // Tolerated: any fenced block that mentions the expected keys.
        foreach (Match match in AnyFencedBlockRegex().Matches(output).Cast<Match>().Reverse())
        {
            string body = match.Groups["json"].Value;

            if (body.Contains("\"status\"", StringComparison.Ordinal) &&
                body.Contains("\"summary\"", StringComparison.Ordinal))
            {
                yield return body;
            }
        }

        // Last resort: a bare object that carries the contract's keys.
        foreach (Match match in BareObjectRegex().Matches(output).Cast<Match>().Reverse())
        {
            yield return match.Value;
        }
    }

    private static bool TryDeserialize(string json, int issueNumber, out AgentReport report)
    {
        try
        {
            RawReport? raw = JsonSerializer.Deserialize<RawReport>(json.Trim(), SerializerOptions);

            if (raw is null || string.IsNullOrWhiteSpace(raw.Summary))
            {
                report = null!;
                return false;
            }

            report = new AgentReport(
                raw.Issue ?? issueNumber,
                ParseStatus(raw.Status),
                raw.Summary,
                raw.FilesChanged ?? [],
                raw.TestsAdded ?? [],
                raw.OutOfScopeNeeded ?? [],
                raw.Risks ?? [],
                string.IsNullOrWhiteSpace(raw.BlockedReason) ? null : raw.BlockedReason);

            return true;
        }
        catch (JsonException)
        {
            report = null!;
            return false;
        }
    }

    private static ImplementationStatus ParseStatus(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "completed" or "complete" or "done" or "success" => ImplementationStatus.Completed,
        "partial" or "incomplete" => ImplementationStatus.Partial,
        "blocked" => ImplementationStatus.Blocked,
        "failed" or "error" => ImplementationStatus.Failed,

        // An unrecognised status must not become a success.
        _ => ImplementationStatus.Partial,
    };

    private static string Summarize(string output)
    {
        string[] lines =
        [
            .. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Where(l => !l.StartsWith("```", StringComparison.Ordinal)),
        ];

        string tail = string.Join(' ', lines.TakeLast(3));

        return tail.Length <= 300 ? tail : tail[^300..];
    }

    [GeneratedRegex(
        @"```[a-zA-Z]*\s+AGENT_REPORT\s*(?<json>\{.*?\})\s*```",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex TaggedBlockRegex();

    [GeneratedRegex(
        @"```(?:json)?\s*(?<json>\{.*?\})\s*```",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex AnyFencedBlockRegex();

    [GeneratedRegex(
        @"\{[^{}]*""status""\s*:\s*""[a-zA-Z]+""[^{}]*(?:\{[^{}]*\}|\[[^\[\]]*\])?[^{}]*\}",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex BareObjectRegex();

    private sealed record RawReport(
        int? Issue,
        string? Status,
        string? Summary,
        IReadOnlyList<string>? FilesChanged,
        IReadOnlyList<string>? TestsAdded,
        IReadOnlyList<string>? OutOfScopeNeeded,
        IReadOnlyList<string>? Risks,
        string? BlockedReason);
}
