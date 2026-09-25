# Instruções do GitHub Copilot — TDC SP 2026 · Agent Squad

Estas instruções valem para **todo** o repositório e são carregadas automaticamente pelo
GitHub Copilot (CLI, coding agent, Chat e code review). Elas complementam — nunca
substituem — o [`AGENTS.md`](../AGENTS.md) e as
[regras de engenharia](../docs/engineering-rules.md).

## Contexto do projeto

Fábrica de software autônoma em **.NET 10**:

- **Orquestração de agentes:** Microsoft Agent Framework (`Microsoft.Agents.AI*`)
- **Modelos:** Microsoft Foundry (Entra ID, sem chave de API)
- **Agentes implementadores:** GitHub Copilot CLI em modo programático
- **Tarefas:** GitHub Issues · **Entrega:** Pull Requests
- **Isolamento:** um `git worktree` por issue
- **Apresentação:** console (Spectre.Console) + web (Blazor Server) sob .NET Aspire

## Como escrever código aqui

- C# 14 / .NET 10, `namespace` file-scoped, nullable habilitado, `ImplicitUsings` ligado.
- Construtor primário quando o tipo só encaminha dependências.
- `record` para dados imutáveis; `sealed class` para serviços.
- `TimeProvider` — **nunca** `DateTime.Now`/`UtcNow` direto.
- `CancellationToken` sempre como último parâmetro e sempre propagado.
- `ILogger<T>` com template estruturado; nunca interpolação no template.
- Configuração via `IOptions<T>` validado na inicialização.
- `System.Text.Json` com `JsonSerializerContext` (source-generated).
- Processos externos (`git`, `gh`, `copilot`, `dotnet`) **somente** através das abstrações
  em `AgentSquad.Tools` — nunca `Process.Start` espalhado pelo código.
- Saída de LLM é **dado não confiável**: valide contra schema antes de usar.

## O que nunca fazer

- Commitar segredo, token, connection string ou `.env`.
- Montar linha de comando por concatenação (`ProcessStartInfo.Arguments`) — use `ArgumentList`.
- Suprimir analisador para fazer o build passar.
- Adicionar `PackageReference` com `Version=` (use Central Package Management).
- `.Result`, `.Wait()`, `async void`, `Thread.Sleep`.
- `git push --force`, reescrita de histórico, push direto em `main`.
- Marcar teste como `Skip` para esverdear o build.

## Testes

xUnit v3 + Microsoft.Testing.Platform + AwesomeAssertions.
Nome: `Metodo_Should_Comportamento_When_Condicao`. Determinismo obrigatório.

## Comandos do dia a dia

```powershell
dotnet restore AgentSquad.slnx
dotnet format  AgentSquad.slnx --verify-no-changes --severity info
dotnet build   AgentSquad.slnx -c Release -warnaserror
dotnet test    AgentSquad.slnx -c Release
dotnet run --project src/AgentSquad.AppHost   # Aspire: API + Blazor + dashboard
dotnet run --project src/AgentSquad.Cli -- --help
```

## Commits e PR

Conventional Commits (`feat(escopo): resumo`), corpo explicando o **porquê**,
`Refs: #<issue>`. PR usa o template do repositório e fecha a issue com `Closes #<n>`.

## Idioma

- **Código, identificadores, mensagens de log e commits:** inglês.
- **Documentação, issues, PRs e comentários voltados ao público da palestra:** português do Brasil.
