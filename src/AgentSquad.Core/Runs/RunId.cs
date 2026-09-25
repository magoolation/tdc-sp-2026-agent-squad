using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace AgentSquad.Core.Runs;

/// <summary>
/// Identifies a single end-to-end execution of the software factory.
/// </summary>
/// <remarks>
/// The value is sortable by creation time and safe to use as a directory name,
/// because every run writes its audit trail to <c>.squad/runs/&lt;runId&gt;/</c> (AI-006).
/// </remarks>
[SuppressMessage("Design", "CA1036:Override methods on comparable types",
    Justification = "Ordering is only used for sorting run folders; relational operators would add no value.")]
public readonly record struct RunId : IComparable<RunId>
{
    private readonly string? _value;

    private RunId(string value) => _value = value;

    /// <summary>Gets the textual form of the identifier.</summary>
    public string Value => _value ?? throw new InvalidOperationException("The run identifier is uninitialized.");

    /// <summary>
    /// Creates a new identifier of the form <c>20260925-143012-a1b2c3</c>.
    /// </summary>
    /// <param name="timeProvider">Clock used to stamp the identifier (TST-004: never <see cref="DateTime"/>.Now).</param>
    /// <returns>A fresh, sortable identifier.</returns>
    public static RunId New(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        string stamp = timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..6];

        return new RunId($"{stamp}-{suffix}");
    }

    /// <summary>Reconstructs an identifier from its textual form.</summary>
    /// <param name="value">A value previously produced by <see cref="Value"/>.</param>
    /// <returns>The parsed identifier.</returns>
    public static RunId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return new RunId(value);
    }

    /// <inheritdoc />
    public int CompareTo(RunId other) => string.CompareOrdinal(_value, other._value);

    /// <inheritdoc />
    public override string ToString() => _value ?? "(uninitialized)";
}
