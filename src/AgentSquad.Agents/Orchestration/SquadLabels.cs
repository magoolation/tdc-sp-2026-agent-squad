using System.Globalization;
using AgentSquad.Core.Abstractions;
using AgentSquad.Core.Planning;

namespace AgentSquad.Agents.Orchestration;

/// <summary>
/// The labels the factory needs on the target repository.
/// </summary>
/// <remarks>
/// Labels are not decoration. <c>wave:</c> is how a human reads the parallelism plan off
/// the issue list, and <c>agent-generated</c> is the transparency marker AI-009 requires
/// on everything the factory opens.
/// </remarks>
public static class SquadLabels
{
    /// <summary>
    /// Builds the label set required by a plan.
    /// </summary>
    /// <param name="plan">The plan about to be published.</param>
    /// <returns>Every label the issues and pull requests will use.</returns>
    public static IReadOnlyList<LabelDefinition> For(DeliveryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var labels = new List<LabelDefinition>
        {
            new("agent-task", "5319E7", "Tarefa destinada a um agente autônomo"),
            new("agent-generated", "8250DF", "Produzido por um agente — o merge continua sendo decisão humana"),
            new("needs-human", "D93F0B", "Exige decisão ou correção humana antes de seguir"),
            new("size:s", "C2E0C6", "Até ~100 linhas de diff útil"),
            new("size:m", "FEF2C0", "Entre ~100 e ~250 linhas de diff útil"),
            new("size:l", "F9D0C4", "Entre ~250 e ~400 linhas de diff útil"),
        };

        foreach (int wave in plan.Items.Select(i => i.Wave).Distinct().Order())
        {
            labels.Add(new LabelDefinition(
                $"wave:{wave.ToString(CultureInfo.InvariantCulture)}",
                WaveColor(wave),
                $"Onda de paralelismo {wave.ToString(CultureInfo.InvariantCulture)}"));
        }

        foreach (string area in plan.Items.Select(i => i.Area).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            labels.Add(new LabelDefinition($"area:{area}", "0E8A16", $"Componente: {area}"));
        }

        return labels;
    }

    private static string WaveColor(int wave) => (wave % 4) switch
    {
        1 => "1D76DB",
        2 => "0052CC",
        3 => "5319E7",
        _ => "B60205",
    };
}
