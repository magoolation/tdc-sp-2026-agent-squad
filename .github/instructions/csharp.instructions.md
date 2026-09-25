---
applyTo: "**/*.cs"
description: "Convenções de C# obrigatórias para todo código deste repositório."
---

# C# — regras aplicadas a todo arquivo `.cs`

## Forma do arquivo

```csharp
// SPDX-License-Identifier: MIT
using System.Diagnostics;                 // usings fora do namespace, System primeiro
using Microsoft.Extensions.Logging;

namespace AgentSquad.Core.Planning;       // file-scoped

/// <summary>Descreve o que o tipo faz, não como.</summary>
internal sealed class WavePlanner(ILogger<WavePlanner> logger, TimeProvider timeProvider)
{
    private readonly ILogger<WavePlanner> _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider;
}
```

- Um tipo público por arquivo. Nome do arquivo = nome do tipo.
- Namespace espelha a estrutura de pastas.
- `sealed` por padrão. `internal` por padrão; `public` só no que é API de verdade.
- Ordem dos membros: constantes → campos → construtores → propriedades → métodos →
  tipos aninhados. Dentro de cada grupo: `public` → `internal` → `protected` → `private`.

## Nulabilidade

- `Nullable` habilitado em todos os projetos; nenhum `#nullable disable`.
- `!` (null-forgiving) exige comentário na linha anterior justificando a invariante.
- Validação de argumento no topo do método:

```csharp
ArgumentNullException.ThrowIfNull(issue);
ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxParallelism);
```

## Assíncrono

```csharp
public async Task<ValidationReport> ValidateAsync(
    Worktree worktree,
    CancellationToken cancellationToken)   // sempre o ÚLTIMO parâmetro
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromMinutes(10));   // todo processo externo tem timeout

    await foreach (var line in _process.RunAsync(command, timeout.Token))
    {
        // ...
    }
}
```

Proibido: `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`, `async void`
(exceto event handler), `Task.Run` para embrulhar código síncrono bloqueante,
`Thread.Sleep` (use `await Task.Delay(..., cancellationToken)`).

## Strings e cultura

```csharp
// certo
if (label.Equals("agent-generated", StringComparison.OrdinalIgnoreCase)) { }
var text = value.ToString(CultureInfo.InvariantCulture);

// errado
if (label.ToLower() == "agent-generated") { }
var text = value.ToString();
```

## Regex

```csharp
[GeneratedRegex(@"^agent/issue-(?<n>\d+)-", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
private static partial Regex BranchPattern();
```

## Logging

```csharp
// certo — template estruturado
_logger.LogInformation("Issue {IssueNumber} atribuída ao worktree {Worktree}", number, path);

// errado — interpolação
_logger.LogInformation($"Issue {number} atribuída ao worktree {path}");
```

Nunca logue token, chave, `Authorization`, PII ou prompt com dado sensível.

## Exceções

- Lance o tipo mais específico. Nunca `throw ex;` — use `throw;`.
- Nunca `catch (Exception)` sem relançar ou sem registrar e tratar de forma explícita.
- Exceções de domínio herdam de uma base do projeto e carregam contexto estruturado.

## Processos externos

```csharp
var psi = new ProcessStartInfo("git")
{
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    WorkingDirectory = worktree.Path,
};
psi.ArgumentList.Add("worktree");          // NUNCA psi.Arguments = "..."
psi.ArgumentList.Add("add");
psi.ArgumentList.Add(worktree.Path);
```

## Coleções

- Entrada: `IEnumerable<T>`. Saída: `IReadOnlyList<T>`/`IReadOnlyDictionary<K,V>`.
- Nunca devolva `null` para coleção — devolva vazia.
- Não enumere duas vezes sem materializar.
- Expressões de coleção (`[]`, `[..a, ..b]`) quando o tipo alvo for claro.
