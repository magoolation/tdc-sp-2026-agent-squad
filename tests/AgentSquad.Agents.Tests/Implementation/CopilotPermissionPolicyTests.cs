using AgentSquad.Agents.Implementation;
using AgentSquad.Core.Configuration;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentSquad.Agents.Tests.Implementation;

/// <summary>
/// Tests for the sandbox that confines a coding agent.
/// </summary>
/// <remarks>
/// This is the security boundary of the whole factory (AI-004): inside its worktree the
/// agent is autonomous, and outside it the agent is powerless. Every test here describes an
/// escape that must not work, so a regression in this file is a regression in the product's
/// safety, not merely in its behaviour.
/// </remarks>
public sealed class CopilotPermissionPolicyTests
{
    private const string Worktree = @"C:\squad\wt\i42";
    private const string Artifacts = @"C:\squad\bld\i42";

    [Fact]
    public async Task Evaluate_Should_ApproveCommit_When_TheCommandIsAllowed()
    {
        // Arrange
        CopilotPermissionPolicy policy = CreatePolicy();
        PermissionRequestShell request = Shell("git commit");

        // Act
        PermissionDecision decision = await policy.EvaluateAsync(request, Invocation());

        // Assert
        decision.Should().BeOfType<PermissionDecisionApproveOnce>();
        policy.Denials.Should().BeEmpty();
    }

    [Fact]
    public async Task Evaluate_Should_RejectPush_When_TheAgentTriesToPublish()
    {
        // Arrange — publication belongs to the orchestrator, after the gate has passed.
        CopilotPermissionPolicy policy = CreatePolicy();
        PermissionRequestShell request = Shell("git push");

        // Act
        PermissionDecision decision = await policy.EvaluateAsync(request, Invocation());

        // Assert
        decision.Should().BeOfType<PermissionDecisionReject>();
        policy.Denials.Should().ContainSingle();
    }

    [Theory]
    [InlineData("git worktree")]
    [InlineData("git checkout")]
    [InlineData("git rebase")]
    [InlineData("git merge")]
    [InlineData("git reset")]
    [InlineData("gh pr")]
    [InlineData("az")]
    public async Task Evaluate_Should_Reject_When_TheCommandBelongsToTheOrchestrator(string command)
    {
        // Arrange
        CopilotPermissionPolicy policy = CreatePolicy();

        // Act
        PermissionDecision decision = await policy.EvaluateAsync(Shell(command), Invocation());

        // Assert
        decision.Should().BeOfType<PermissionDecisionReject>();
    }

    [Fact]
    public async Task Evaluate_Should_RejectShellRedirection_When_ItWouldBypassThePathCheck()
    {
        // Arrange — a redirection writes a file without going through the write permission,
        // which would let an agent write outside the sandbox unobserved.
        CopilotPermissionPolicy policy = CreatePolicy();

        PermissionRequestShell request = Shell("echo", redirection: true);

        // Act
        PermissionDecision decision = await policy.EvaluateAsync(request, Invocation());

        // Assert
        decision.Should().BeOfType<PermissionDecisionReject>();
    }

    [Fact]
    public async Task Evaluate_Should_ApproveWrite_When_ThePathIsInsideTheWorktree()
    {
        // Arrange
        CopilotPermissionPolicy policy = CreatePolicy();
        PermissionRequestWrite request = Write(Path.Combine(Worktree, "src", "Order.cs"));

        // Act
        PermissionDecision decision = await policy.EvaluateAsync(request, Invocation());

        // Assert
        decision.Should().BeOfType<PermissionDecisionApproveOnce>();
    }

    [Fact]
    public async Task Evaluate_Should_RejectWrite_When_ThePathEscapesTheWorktree()
    {
        // Arrange
        CopilotPermissionPolicy policy = CreatePolicy();
        PermissionRequestWrite request = Write(@"C:\squad\wt\i43\src\Order.cs");

        // Act
        PermissionDecision decision = await policy.EvaluateAsync(request, Invocation());

        // Assert — a sibling agent's worktree is as off-limits as the rest of the machine.
        decision.Should().BeOfType<PermissionDecisionReject>();
    }

    [Fact]
    public async Task Evaluate_Should_RejectWrite_When_ThePathTraversesOutOfTheWorktree()
    {
        // Arrange — the classic escape: a path that only looks contained.
        CopilotPermissionPolicy policy = CreatePolicy();
        PermissionRequestWrite request = Write(Path.Combine(Worktree, "..", "i43", "Order.cs"));

        // Act
        PermissionDecision decision = await policy.EvaluateAsync(request, Invocation());

        // Assert
        decision.Should().BeOfType<PermissionDecisionReject>();
    }

    [Fact]
    public async Task Evaluate_Should_RejectWrite_When_ASiblingDirectoryShareseTheWorktreePrefix()
    {
        // Arrange — "C:\squad\wt\i42-evil" starts with "C:\squad\wt\i42" as a string but is
        // a different directory. A naive prefix comparison would let this through.
        CopilotPermissionPolicy policy = CreatePolicy();
        PermissionRequestWrite request = Write(@"C:\squad\wt\i42-evil\Order.cs");

        // Act
        PermissionDecision decision = await policy.EvaluateAsync(request, Invocation());

        // Assert
        decision.Should().BeOfType<PermissionDecisionReject>();
    }

    [Fact]
    public async Task Evaluate_Should_ApproveWrite_When_ThePathIsTheArtifactsDirectory()
    {
        // Arrange — build output lives outside the worktree by design, to keep obj/bin from
        // polluting the diff, so the agent must still be able to write there.
        CopilotPermissionPolicy policy = CreatePolicy();
        PermissionRequestWrite request = Write(Path.Combine(Artifacts, "obj", "project.assets.json"));

        // Act
        PermissionDecision decision = await policy.EvaluateAsync(request, Invocation());

        // Assert
        decision.Should().BeOfType<PermissionDecisionApproveOnce>();
    }

    [Fact]
    public async Task Evaluate_Should_ApproveFetch_When_TheHostIsOnTheAllowList()
    {
        // Arrange
        CopilotPermissionPolicy policy = CreatePolicy();
        PermissionRequestUrl request = Url("https://learn.microsoft.com/dotnet/csharp");

        // Act
        PermissionDecision decision = await policy.EvaluateAsync(request, Invocation());

        // Assert
        decision.Should().BeOfType<PermissionDecisionApproveOnce>();
    }

    [Fact]
    public async Task Evaluate_Should_RejectFetch_When_TheHostIsNotOnTheAllowList()
    {
        // Arrange — arbitrary egress is the exfiltration path (AI-003).
        CopilotPermissionPolicy policy = CreatePolicy();
        PermissionRequestUrl request = Url("https://exfiltrate.example.com/collect");

        // Act
        PermissionDecision decision = await policy.EvaluateAsync(request, Invocation());

        // Assert
        decision.Should().BeOfType<PermissionDecisionReject>();
    }

    [Fact]
    public async Task Evaluate_Should_RejectFetch_When_TheHostOnlySuffixesAnAllowedDomain()
    {
        // Arrange — "learn.microsoft.com.evil.test" must not satisfy "learn.microsoft.com".
        CopilotPermissionPolicy policy = CreatePolicy();
        PermissionRequestUrl request = Url("https://learn.microsoft.com.evil.test/x");

        // Act
        PermissionDecision decision = await policy.EvaluateAsync(request, Invocation());

        // Assert
        decision.Should().BeOfType<PermissionDecisionReject>();
    }

    [Fact]
    public async Task Evaluate_Should_RejectRead_When_TheFileLooksLikeACredentialStore()
    {
        // Arrange
        CopilotPermissionPolicy policy = CreatePolicy();
        PermissionRequestRead request = Read(@"C:\Users\someone\.ssh\id_rsa");

        // Act
        PermissionDecision decision = await policy.EvaluateAsync(request, Invocation());

        // Assert
        decision.Should().BeOfType<PermissionDecisionReject>();
    }

    [Fact]
    public async Task Evaluate_Should_ApproveReadOnlyMcpTool_ButRejectAMutatingOne()
    {
        // Arrange
        CopilotPermissionPolicy policy = CreatePolicy();

        // Act
        PermissionDecision readOnly = await policy.EvaluateAsync(
            Mcp("docs", "search", readOnly: true), Invocation());

        PermissionDecision mutating = await policy.EvaluateAsync(
            Mcp("deploy", "release", readOnly: false), Invocation());

        // Assert
        readOnly.Should().BeOfType<PermissionDecisionApproveOnce>();
        mutating.Should().BeOfType<PermissionDecisionReject>();
    }

    [Fact]
    public async Task Evaluate_Should_DeclineWithoutRejecting_When_ManagedPolicyRequiresAHuman()
    {
        // Arrange — auto-approving would override an organizational control, and rejecting
        // would deny something a human might legitimately allow. Declining leaves it open.
        CopilotPermissionPolicy policy = CreatePolicy();

        PermissionRequestShell request = Shell("dotnet build", managedApproval: true);

        // Act
        PermissionDecision decision = await policy.EvaluateAsync(request, Invocation());

        // Assert
        decision.Should().BeOfType<PermissionDecisionNoResult>();
    }

    [Fact]
    public async Task Evaluate_Should_RecordEveryDenial_ForTheAuditTrail()
    {
        // Arrange — a policy that blocks silently produces an agent that fails for reasons
        // nobody can see (AI-006).
        CopilotPermissionPolicy policy = CreatePolicy();

        // Act
        await policy.EvaluateAsync(Shell("git push"), Invocation());
        await policy.EvaluateAsync(Url("https://evil.test/x"), Invocation());

        // Assert
        policy.Denials.Should().HaveCount(2);
    }

    // -------------------------------------------------------------------------------------

    private static CopilotPermissionPolicy CreatePolicy() =>
        new(new CopilotOptions(), Worktree, Artifacts, NullLogger.Instance);

    // The SDK's permission request types carry `required` members that this policy never
    // reads. These builders fill them with inert values so each test states only the one
    // thing it is actually about.

    private static PermissionRequestShell Shell(
        string identifier,
        bool redirection = false,
        bool managedApproval = false) =>
        new()
        {
            FullCommandText = identifier,
            Commands = [new PermissionRequestShellCommand { Identifier = identifier, ReadOnly = false }],
            CommandSegments = [],
            PossiblePaths = [],
            ResolvedPaths = new Dictionary<string, string>(StringComparer.Ordinal),
            PossibleUrls = [],
            CanOfferSessionApproval = true,
            Intention = "test",
            HasWriteFileRedirection = redirection,
            ManagedApprovalRequired = managedApproval,
        };

    private static PermissionRequestWrite Write(string path) =>
        new()
        {
            FileName = Path.GetFileName(path),
            ResolvedPath = path,
            Diff = string.Empty,
            CanOfferSessionApproval = true,
            Intention = "test",
        };

    private static PermissionRequestRead Read(string path) =>
        new()
        {
            Path = path,
            ResolvedPath = path,
            Intention = "test",
        };

    private static PermissionRequestUrl Url(string url) =>
        new()
        {
            Url = url,
            Intention = "test",
        };

    private static PermissionRequestMcp Mcp(string server, string tool, bool readOnly) =>
        new()
        {
            ServerName = server,
            ToolName = tool,
            ToolTitle = tool,
            ReadOnly = readOnly,
        };

    private static PermissionInvocation Invocation() => new();
}


