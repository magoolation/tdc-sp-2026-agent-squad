---
name: github-issue-authoring
description: Como fatiar um plano de execução em GitHub issues que agentes autônomos consigam implementar em paralelo sem conflito - tamanho, escopo de arquivos, critérios de aceite, dependências e ondas de paralelismo. Use ao converter o plano aprovado em tarefas.
---

# Skill: escrita de issues para agentes paralelos

## O que muda em relação a uma issue "para humano"

Um humano preenche lacunas conversando. Um agente autônomo, não. A issue precisa ser
**auto-contida e executável**: quem a lê tem apenas ela, o `AGENTS.md`, as regras de
engenharia e o repositório.

## Critérios de fatiamento

Uma issue boa para um agente é:

| Critério | Alvo |
|---|---|
| Tamanho do diff | ~50 a 400 linhas úteis |
| Arquivos tocados | 1 a 6, **declarados na issue** |
| Duração | uma execução de agente (< 20 min) |
| Verificabilidade | o gate de validação decide sozinho se passou |
| Acoplamento | zero sobreposição de arquivos com issues da **mesma onda** |

### A regra do conflito de arquivos

**Duas issues na mesma onda de paralelismo nunca podem declarar o mesmo arquivo.**

Esta é a regra mais importante da fábrica. Se dois agentes editam `OrderService.cs` ao
mesmo tempo em worktrees diferentes, os dois PRs vão conflitar e o paralelismo vira
retrabalho. Quando houver sobreposição, escolha uma:

1. **Sequenciar:** colocar uma issue numa onda posterior com `dependsOn`.
2. **Refatorar antes:** uma issue prévia extrai a interface/partial que permite paralelizar.
3. **Fundir:** se as duas são pequenas e tocam o mesmo arquivo, vire uma issue só.

### Ondas

```text
Onda 1  →  fundações sem dependência: modelo de domínio, contratos, infra de projeto
Onda 2  →  implementações que dependem da onda 1: serviços, repositórios, endpoints
Onda 3  →  integração e superfície: UI, wiring de DI, documentação, e2e
```

Cada onda só começa depois que **todos** os PRs da anterior foram integrados.

## Template de issue

```markdown
## Objetivo

<Uma frase: qual comportamento observável passa a existir.>

## Contexto

<Por que isso é necessário. Link para o plano e para as issues relacionadas.
Se for brownfield, cite os arquivos existentes que estabelecem o padrão a seguir.>

## Escopo — arquivos previstos

| Arquivo | Ação |
|---|---|
| `src/AgentSquad.Core/Planning/WavePlanner.cs` | criar |
| `tests/AgentSquad.Core.Tests/Planning/WavePlannerTests.cs` | criar |

**Fora de escopo:** <o que explicitamente NÃO deve ser tocado nesta issue.>

## Critérios de aceite

- [ ] Dado <contexto>, quando <ação>, então <resultado observável>
- [ ] Dado <contexto de erro>, quando <ação>, então <erro tratado de forma X>
- [ ] Cobertura de teste para o caminho feliz e para pelo menos um caminho de falha
- [ ] `dotnet format --verify-no-changes`, `dotnet build -warnaserror` e `dotnet test` verdes

## Notas de implementação

<Padrão a seguir, armadilha conhecida, decisão de design já tomada no plano.
Não escreva o código aqui — escreva a restrição.>

## Definição de pronto

PR aberto, gate verde, revisão automatizada sem finding bloqueante, `Closes #<n>`.
```

## Labels obrigatórias

| Label | Uso |
|---|---|
| `agent-task` | a issue é destinada a um agente autônomo |
| `wave:1` / `wave:2` / `wave:3` | onda de paralelismo |
| `area:core` / `area:agents` / `area:web` / `area:cli` / `area:infra` / `area:docs` | componente |
| `size:s` / `size:m` / `size:l` | tamanho estimado do diff |
| `needs-human` | exige decisão humana antes de ser executada |

## Dependências

Declare no corpo, em bloco parseável:

```yaml
<!-- squad:meta
wave: 2
dependsOn: [12, 13]
files:
  - src/AgentSquad.Agents/Implementation/CopilotCliAgent.cs
  - tests/AgentSquad.Agents.Tests/Implementation/CopilotCliAgentTests.cs
estimate: m
-->
```

O orquestrador lê esse bloco para montar o grafo e validar a regra do conflito de arquivos.

## Checklist antes de publicar o conjunto de issues

- [ ] Todo critério de aceite é verificável por máquina.
- [ ] Nenhum par de issues da mesma onda declara o mesmo arquivo.
- [ ] Toda dependência aponta para uma issue de onda **anterior** (grafo acíclico).
- [ ] Nenhuma issue ultrapassa ~6 arquivos ou ~400 linhas estimadas.
- [ ] Todo requisito funcional do plano está coberto por pelo menos uma issue.
- [ ] Nenhuma issue depende de informação que só existe na conversa (tudo está escrita nela).
- [ ] Issues que precisam de decisão humana estão marcadas `needs-human`.

## Anti-padrões

| Anti-padrão | Consequência |
|---|---|
| "Implementar o módulo de pagamentos" | Grande demais; o agente se perde e o PR é irreversível |
| Critério de aceite "deve funcionar corretamente" | Não verificável; o gate não decide |
| Duas issues na mesma onda tocando `Program.cs` | Conflito garantido |
| Issue sem lista de arquivos | Impossível validar conflito; o agente extrapola escopo |
| Dependência circular entre issues | A fábrica trava |
| "Refatorar tudo enquanto isso" embutido numa issue de feature | Diff gigante, revisão impossível |
