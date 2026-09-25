using System.Globalization;
using System.Text;

namespace AgentSquad.Agents.Orchestration;

/// <summary>
/// Summarizes a repository so the intake agent can classify it without reading every file.
/// </summary>
/// <remarks>
/// <para>
/// Two things are kept deliberately bounded: the number of paths listed and the size of the
/// files whose contents are included. An unbounded inventory would push the interesting
/// signal out of the model's attention and cost tokens for no benefit.
/// </para>
/// <para>
/// The files whose contents are included are the ones that establish conventions — project
/// files, <c>Directory.Build.props</c>, <c>.editorconfig</c>, <c>AGENTS.md</c> — because a
/// convention the agent can quote is worth more than a hundred paths it can only guess from.
/// </para>
/// </remarks>
public static class RepositoryInventory
{
    private const int MaxPaths = 400;
    private const int MaxFileBytes = 8_000;

    private static readonly string[] SkipDirectories =
    [
        ".git", "bin", "obj", "node_modules", ".vs", ".idea", "packages",
        "artifacts", "TestResults", ".worktrees", ".squad", "dist",
    ];

    private static readonly string[] InterestingFiles =
    [
        "AGENTS.md", "README.md", "global.json", "Directory.Build.props",
        "Directory.Packages.props", ".editorconfig", "azure.yaml",
        "package.json", "pyproject.toml", "go.mod", "Cargo.toml", "pom.xml",
    ];

    /// <summary>
    /// Describes a repository.
    /// </summary>
    /// <param name="repositoryPath">Absolute path of the local clone.</param>
    /// <returns>A Markdown inventory suitable for a prompt.</returns>
    public static string Describe(string repositoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        if (!Directory.Exists(repositoryPath))
        {
            return "O diretório do repositório não existe.";
        }

        var builder = new StringBuilder();
        List<string> paths = [.. EnumeratePaths(repositoryPath).Take(MaxPaths + 1)];

        if (paths.Count == 0)
        {
            return "O repositório está vazio — não há nenhum arquivo versionado além, possivelmente, do `.git`.";
        }

        builder.AppendLine("### Estrutura").AppendLine();
        builder.AppendLine("```text");

        foreach (string path in paths.Take(MaxPaths))
        {
            builder.AppendLine(path);
        }

        if (paths.Count > MaxPaths)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"... (mais de {MaxPaths} arquivos; listagem truncada)");
        }

        builder.AppendLine("```").AppendLine();

        builder.AppendLine("### Contagem por extensão").AppendLine();

        IEnumerable<IGrouping<string, string?>> byExtension = paths
            .Select(Path.GetExtension)
            .Where(e => !string.IsNullOrEmpty(e))
            .GroupBy(e => e!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(12);

        foreach (IGrouping<string, string?>? group in byExtension)
        {
            builder.Append("- `").Append(group.Key).Append("`: ")
                   .Append(group.Count().ToString(CultureInfo.InvariantCulture)).AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("### Arquivos que estabelecem convenções").AppendLine();

        bool any = false;

        foreach (string candidate in InterestingFiles)
        {
            string full = Path.Combine(repositoryPath, candidate);

            if (!File.Exists(full))
            {
                continue;
            }

            any = true;
            builder.Append("#### `").Append(candidate).AppendLine("`").AppendLine();
            builder.AppendLine("```");
            builder.AppendLine(ReadBounded(full));
            builder.AppendLine("```").AppendLine();
        }

        // A project file is the single most informative artifact in a .NET repository:
        // target framework, analyzer settings, package set and test stack, all in one place.
        string? project = paths.FirstOrDefault(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));

        if (project is not null)
        {
            any = true;
            builder.Append("#### `").Append(project).AppendLine("`").AppendLine();
            builder.AppendLine("```xml");
            builder.AppendLine(ReadBounded(Path.Combine(repositoryPath, project)));
            builder.AppendLine("```").AppendLine();
        }

        if (!any)
        {
            builder.AppendLine("_(nenhum arquivo de convenção reconhecido)_");
        }

        return builder.ToString();
    }

    private static IEnumerable<string> EnumeratePaths(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            string[] files;
            string[] directories;

            try
            {
                files = Directory.GetFiles(current);
                directories = Directory.GetDirectories(current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException)
            {
                continue;
            }

            foreach (string file in files)
            {
                yield return Path.GetRelativePath(root, file).Replace('\\', '/');
            }

            foreach (string directory in directories)
            {
                string name = Path.GetFileName(directory);

                if (!SkipDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    pending.Push(directory);
                }
            }
        }
    }

    private static string ReadBounded(string path)
    {
        try
        {
            var info = new FileInfo(path);

            if (info.Length <= MaxFileBytes)
            {
                return File.ReadAllText(path);
            }

            using var reader = new StreamReader(path);
            char[] buffer = new char[MaxFileBytes];
            int read = reader.ReadBlock(buffer, 0, MaxFileBytes);

            return new string(buffer, 0, read) + "\n... (truncado)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"(não foi possível ler: {ex.Message})";
        }
    }
}
