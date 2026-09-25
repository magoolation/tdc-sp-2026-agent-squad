using System.ComponentModel.DataAnnotations;
using System.Globalization;
using AgentSquad.Core.Configuration;

namespace AgentSquad.Core.Tests.Configuration;

/// <summary>
/// Tests for the model-call budget.
/// </summary>
/// <remarks>
/// The SDK default of 100 seconds failed a real run: the architect, a reasoning model
/// producing a 22-requirement delivery plan as structured output, went past it, and the
/// retry policy spent four attempts hitting the same wall before failing after nine minutes
/// with a message about network timeouts. The value is a per-attempt budget, so it decides
/// both how long a slow call may take and how long a stuck one holds the run silent.
/// </remarks>
public sealed class FoundryOptionsTests
{
    [Fact]
    public void RequestTimeout_Should_DefaultWellAboveTheSdkDefault()
    {
        var options = new FoundryOptions();

        options.RequestTimeout.Should().BeGreaterThan(
            TimeSpan.FromSeconds(100),
            "100 seconds is the SDK default, and it is the value that failed a real run");
    }

    [Fact]
    public void RequestTimeout_Should_StayBoundedSoAStuckCallCannotHoldTheRunForever()
    {
        var options = new FoundryOptions();

        options.RequestTimeout.Should().BeLessThanOrEqualTo(
            TimeSpan.FromMinutes(5),
            "the retry policy makes four attempts, so this also bounds the silent worst case");
    }

    [Theory]
    [InlineData("00:00:29")]
    [InlineData("00:31:00")]
    public void RequestTimeout_Should_BeRejected_When_OutsideTheAllowedRange(string value)
    {
        FoundryOptions options = Valid();
        options.RequestTimeout = TimeSpan.Parse(value, CultureInfo.InvariantCulture);

        Validate(options).Should().NotBeEmpty();
    }

    [Fact]
    public void RequestTimeout_Should_BeAccepted_When_InsideTheAllowedRange()
    {
        FoundryOptions options = Valid();

        Validate(options).Should().BeEmpty();
    }

    private static FoundryOptions Valid() => new()
    {
        ProjectEndpoint = "https://example.services.ai.azure.com/api/projects/agent-squad",
    };

    private static List<ValidationResult> Validate(FoundryOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
