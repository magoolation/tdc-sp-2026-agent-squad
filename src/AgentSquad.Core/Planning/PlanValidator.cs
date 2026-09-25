using System.Globalization;

namespace AgentSquad.Core.Planning;

/// <summary>Severity of a problem found in a delivery plan.</summary>
public enum PlanIssueSeverity
{
    /// <summary>Worth knowing, but the plan can still be executed.</summary>
    Warning = 0,

    /// <summary>The plan cannot be executed safely and must be revised.</summary>
    Error = 1,
}

/// <summary>
/// A problem found in a delivery plan.
/// </summary>
/// <param name="Severity">Whether this blocks execution.</param>
/// <param name="Code">Stable code such as <c>PLAN001</c>, so the planner agent can be told precisely what to fix.</param>
/// <param name="Message">Human-readable description.</param>
/// <param name="ItemKeys">Work items involved.</param>
public sealed record PlanIssue(
    PlanIssueSeverity Severity,
    string Code,
    string Message,
    IReadOnlyList<string> ItemKeys);

/// <summary>
/// Checks that a plan is actually safe to execute with parallel agents.
/// </summary>
/// <remarks>
/// <para>
/// This is the single most important piece of domain logic in the factory. A language model
/// produces the plan; this deterministic check decides whether the plan may run (AI-008).
/// </para>
/// <para>
/// The rule that matters most is <c>PLAN001</c>: two work items in the same wave may never
/// declare the same file. Two agents editing one file in two worktrees produce two pull
/// requests that conflict, which turns parallelism into rework.
/// </para>
/// </remarks>
public static class PlanValidator
{
    /// <summary>Codes emitted by <see cref="Validate"/>.</summary>
    public static class Codes
    {
        /// <summary>Two items in the same wave declare the same file.</summary>
        public const string FileConflict = "PLAN001";

        /// <summary>An item depends on another item in the same or a later wave.</summary>
        public const string WaveOrdering = "PLAN002";

        /// <summary>The dependency graph contains a cycle.</summary>
        public const string DependencyCycle = "PLAN003";

        /// <summary>An item depends on a key that does not exist in the plan.</summary>
        public const string UnknownDependency = "PLAN004";

        /// <summary>An item declares no files, so conflicts cannot be checked.</summary>
        public const string NoFiles = "PLAN005";

        /// <summary>An item has no machine-verifiable acceptance criteria.</summary>
        public const string NoAcceptanceCriteria = "PLAN006";

        /// <summary>An item is too large to review or to implement in one agent run.</summary>
        public const string ItemTooLarge = "PLAN007";

        /// <summary>Two items share a plan key.</summary>
        public const string DuplicateKey = "PLAN008";

        /// <summary>Waves are not a contiguous sequence starting at one.</summary>
        public const string WaveNumbering = "PLAN009";

        /// <summary>A requirement is not covered by any work item.</summary>
        public const string UncoveredRequirement = "PLAN010";

        /// <summary>An item declares no tests.</summary>
        public const string NoTests = "PLAN011";
    }

    /// <summary>Maximum number of files a single work item may declare.</summary>
    public const int MaxFilesPerItem = 8;

    /// <summary>
    /// Validates a plan and returns every problem found.
    /// </summary>
    /// <param name="plan">The plan to check.</param>
    /// <param name="requirementIds">
    /// Requirement identifiers that must each be covered by at least one item.
    /// Pass an empty collection to skip the coverage check.
    /// </param>
    /// <returns>All issues, most severe first. An empty list means the plan may run.</returns>
    public static IReadOnlyList<PlanIssue> Validate(DeliveryPlan plan, IEnumerable<string> requirementIds)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(requirementIds);

        var issues = new List<PlanIssue>();
        IReadOnlyList<WorkItem> items = plan.Items;

        CheckDuplicateKeys(items, issues);
        CheckWaveNumbering(items, issues);
        CheckPerItemRules(items, issues);
        CheckFileConflicts(items, issues);
        CheckDependencies(items, issues);
        CheckRequirementCoverage(items, requirementIds, issues);

        return [.. issues.OrderByDescending(i => i.Severity).ThenBy(i => i.Code, StringComparer.Ordinal)];
    }

    private static void CheckDuplicateKeys(IReadOnlyList<WorkItem> items, List<PlanIssue> issues)
    {
        foreach (IGrouping<string, WorkItem>? group in items.GroupBy(i => i.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            issues.Add(new PlanIssue(
                PlanIssueSeverity.Error,
                Codes.DuplicateKey,
                $"The key '{group.Key}' is used by {group.Count()} work items. Keys must be unique.",
                [group.Key]));
        }
    }

    private static void CheckWaveNumbering(IReadOnlyList<WorkItem> items, List<PlanIssue> issues)
    {
        if (items.Count == 0)
        {
            return;
        }

        int[] waves = [.. items.Select(i => i.Wave).Distinct().Order()];

        if (waves[0] != 1)
        {
            issues.Add(new PlanIssue(
                PlanIssueSeverity.Error,
                Codes.WaveNumbering,
                $"Waves must start at 1, but the lowest wave in the plan is {waves[0].ToString(CultureInfo.InvariantCulture)}.",
                []));
            return;
        }

        for (int i = 1; i < waves.Length; i++)
        {
            if (waves[i] != waves[i - 1] + 1)
            {
                issues.Add(new PlanIssue(
                    PlanIssueSeverity.Error,
                    Codes.WaveNumbering,
                    $"Wave numbers must be contiguous, but the plan jumps from {waves[i - 1]} to {waves[i]}.",
                    []));
                return;
            }
        }
    }

    private static void CheckPerItemRules(IReadOnlyList<WorkItem> items, List<PlanIssue> issues)
    {
        foreach (WorkItem item in items)
        {
            if (item.Files.Count == 0)
            {
                issues.Add(new PlanIssue(
                    PlanIssueSeverity.Error,
                    Codes.NoFiles,
                    $"'{item.Key}' declares no files. Without a file list the orchestrator cannot guarantee that parallel agents will not collide.",
                    [item.Key]));
            }
            else if (item.Files.Count > MaxFilesPerItem)
            {
                issues.Add(new PlanIssue(
                    PlanIssueSeverity.Error,
                    Codes.ItemTooLarge,
                    $"'{item.Key}' declares {item.Files.Count} files; the limit is {MaxFilesPerItem}. Split it.",
                    [item.Key]));
            }

            if (item.AcceptanceCriteria.Count == 0)
            {
                issues.Add(new PlanIssue(
                    PlanIssueSeverity.Error,
                    Codes.NoAcceptanceCriteria,
                    $"'{item.Key}' has no acceptance criteria, so the validation gate cannot decide whether it is done.",
                    [item.Key]));
            }

            bool touchesProductionCode = item.Files.Any(f =>
                f.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                !f.Path.Contains("tests/", StringComparison.OrdinalIgnoreCase));

            bool hasTests = item.Files.Any(f => f.Path.Contains("tests/", StringComparison.OrdinalIgnoreCase));

            if (touchesProductionCode && !hasTests)
            {
                issues.Add(new PlanIssue(
                    PlanIssueSeverity.Warning,
                    Codes.NoTests,
                    $"'{item.Key}' changes production code but declares no test file (TST-001).",
                    [item.Key]));
            }
        }
    }

    private static void CheckFileConflicts(IReadOnlyList<WorkItem> items, List<PlanIssue> issues)
    {
        foreach (IGrouping<int, WorkItem> wave in items.GroupBy(i => i.Wave))
        {
            var owners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (WorkItem item in wave)
            {
                foreach (string path in item.Paths)
                {
                    string normalized = path.Replace('\\', '/').Trim('/');

                    if (!owners.TryGetValue(normalized, out List<string>? keys))
                    {
                        keys = [];
                        owners[normalized] = keys;
                    }

                    keys.Add(item.Key);
                }
            }

            foreach ((string path, List<string> keys) in owners.Where(kvp => kvp.Value.Count > 1))
            {
                issues.Add(new PlanIssue(
                    PlanIssueSeverity.Error,
                    Codes.FileConflict,
                    $"Wave {wave.Key.ToString(CultureInfo.InvariantCulture)}: '{path}' is claimed by {string.Join(", ", keys)}. " +
                    "Two agents cannot edit the same file in parallel. Sequence them into different waves, extract a seam first, or merge the items.",
                    [.. keys]));
            }
        }
    }

    private static void CheckDependencies(IReadOnlyList<WorkItem> items, List<PlanIssue> issues)
    {
        var byKey = items
            .GroupBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (WorkItem item in items)
        {
            foreach (string dependency in item.DependsOn)
            {
                if (!byKey.TryGetValue(dependency, out WorkItem? target))
                {
                    issues.Add(new PlanIssue(
                        PlanIssueSeverity.Error,
                        Codes.UnknownDependency,
                        $"'{item.Key}' depends on '{dependency}', which is not in the plan.",
                        [item.Key]));
                    continue;
                }

                if (target.Wave >= item.Wave)
                {
                    issues.Add(new PlanIssue(
                        PlanIssueSeverity.Error,
                        Codes.WaveOrdering,
                        $"'{item.Key}' (wave {item.Wave}) depends on '{target.Key}' (wave {target.Wave}). " +
                        "A dependency must land in an earlier wave.",
                        [item.Key, target.Key]));
                }
            }
        }

        foreach (IReadOnlyList<string> cycle in FindCycles(byKey))
        {
            issues.Add(new PlanIssue(
                PlanIssueSeverity.Error,
                Codes.DependencyCycle,
                $"Dependency cycle: {string.Join(" -> ", cycle)}.",
                cycle));
        }
    }

    private static List<IReadOnlyList<string>> FindCycles(Dictionary<string, WorkItem> byKey)
    {
        var cycles = new List<IReadOnlyList<string>>();
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 0 unseen, 1 on stack, 2 done
        var stack = new List<string>();

        foreach (string key in byKey.Keys)
        {
            Visit(key);
        }

        return cycles;

        void Visit(string key)
        {
            if (state.TryGetValue(key, out int seen))
            {
                if (seen == 1)
                {
                    int start = stack.FindIndex(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                    cycles.Add([.. stack[start..], key]);
                }

                return;
            }

            state[key] = 1;
            stack.Add(key);

            if (byKey.TryGetValue(key, out WorkItem? item))
            {
                foreach (string dependency in item.DependsOn)
                {
                    if (byKey.ContainsKey(dependency))
                    {
                        Visit(dependency);
                    }
                }
            }

            stack.RemoveAt(stack.Count - 1);
            state[key] = 2;
        }
    }

    private static void CheckRequirementCoverage(
        IReadOnlyList<WorkItem> items,
        IEnumerable<string> requirementIds,
        List<PlanIssue> issues)
    {
        HashSet<string> covered = [.. items.SelectMany(i => i.RequirementIds), .. Enumerable.Empty<string>()];
        covered = new HashSet<string>(covered, StringComparer.OrdinalIgnoreCase);

        foreach (string requirement in requirementIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!covered.Contains(requirement))
            {
                issues.Add(new PlanIssue(
                    PlanIssueSeverity.Warning,
                    Codes.UncoveredRequirement,
                    $"Requirement '{requirement}' is not referenced by any work item.",
                    []));
            }
        }
    }
}
