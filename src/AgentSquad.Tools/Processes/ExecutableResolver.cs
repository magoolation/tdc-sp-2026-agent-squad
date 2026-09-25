using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace AgentSquad.Tools.Processes;

/// <summary>
/// How to actually launch a command that may be a Windows batch shim.
/// </summary>
/// <param name="FileName">The executable to start.</param>
/// <param name="PrefixArguments">Arguments that must precede the caller's own.</param>
public readonly record struct LaunchPlan(string FileName, IReadOnlyList<string> PrefixArguments);

/// <summary>
/// Resolves a command name to something <see cref="System.Diagnostics.Process"/> can actually start.
/// </summary>
/// <remarks>
/// <para>
/// On Windows, several tools the factory depends on are installed as batch shims rather than
/// native executables — <c>az.cmd</c> from the Azure CLI, and anything installed by npm.
/// <see cref="System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo)"/> with
/// <c>UseShellExecute = false</c> cannot launch a <c>.cmd</c> or <c>.bat</c>: it fails with
/// "is not a valid Win32 application". The symptom is a prerequisite check that reports a
/// perfectly working tool as missing.
/// </para>
/// <para>
/// The fix is to run the shim through <c>cmd.exe /d /c</c>. Note the absence of <c>/s</c>:
/// with <c>/s</c>, <c>cmd</c> strips the outer quotes from the rest of the line and a path
/// containing spaces — <c>C:\Program Files\...</c> — breaks apart. Verified empirically;
/// <c>/d /c</c> with a proper argument list works and <c>/d /s /c</c> with one does not.
/// </para>
/// <para>
/// Arguments are still passed as a list, never as a concatenated command line, so SEC-004
/// holds for the shim path exactly as it does for a native executable.
/// </para>
/// </remarks>
public static class ExecutableResolver
{
    private static readonly ConcurrentDictionary<string, LaunchPlan> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Works out how to launch a command.
    /// </summary>
    /// <param name="fileName">Command name, such as <c>az</c>, or a full path.</param>
    /// <returns>The launch plan. Falls back to the name unchanged when nothing can be resolved.</returns>
    public static LaunchPlan Resolve(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        return Cache.GetOrAdd(fileName, static name =>
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new LaunchPlan(name, []);
            }

            string? resolved = FindOnPath(name);

            if (resolved is null)
            {
                // Let Process.Start produce its own error; this resolver does not invent one.
                return new LaunchPlan(name, []);
            }

            string extension = Path.GetExtension(resolved);

            bool isBatch = extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
                           extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);

            return isBatch
                ? new LaunchPlan("cmd.exe", ["/d", "/c", resolved])
                : new LaunchPlan(resolved, []);
        });
    }

    private static string? FindOnPath(string name)
    {
        if (Path.IsPathRooted(name))
        {
            return File.Exists(name) ? name : null;
        }

        string[] extensions = Path.HasExtension(name)
            ? [string.Empty]
            : [
                .. (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
              ];

        string[] directories =
        [
            Directory.GetCurrentDirectory(),
            .. (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        ];

        foreach (string directory in directories)
        {
            foreach (string extension in extensions)
            {
                string candidate;

                try
                {
                    candidate = Path.Combine(directory, name + extension);
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry. Skip it rather than failing the whole lookup.
                    continue;
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
