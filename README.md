# Agent Squad

**Uma fábrica de software autônoma.** Você descreve o que precisa — ou entrega a
transcrição de uma reunião de levantamento — e ela entende o repositório, levanta os
requisitos, pergunta o que for ambíguo, monta um plano para a sua aprovação, publica as
tarefas como **GitHub issues**, dispara **agentes de codificação em paralelo** (cada um em
seu próprio `git worktree`), valida tudo com linters, analisadores e testes, e abre um
**pull request por tarefa** para revisão humana.

> **Demo do TDC São Paulo 2026.**
> .NET 10 · Microsoft Agent Framework 1.22 · Microsoft Foundry · GitHub Copilot SDK ·
> Aspire 13.5 · Blazor Server

---

## Índice

1. [O que ela faz](#1-o-que-ela-faz)
2. [Arquitetura](#2-arquitetura)
3. [Pré-requisitos](#3-pré-requisitos)
4. [Replicando do zero](#4-replicando-do-zero)
5. [Executando](#5-executando)
6. [A camada de personalização](#6-a-camada-de-personalização)
7. [Regras da empresa, aplicadas de verdade](#7-regras-da-empresa-aplicadas-de-verdade)
8. [Modelo de segurança](#8-modelo-de-segurança)
9. [Bônus: da reunião ao pull request](#9-bônus-da-reunião-ao-pull-request)
10. [O que descobrimos construindo isto](#10-o-que-descobrimos-construindo-isto)
11. [Solução de problemas](#11-solução-de-problemas)
12. [Roteiro da apresentação](#12-roteiro-da-apresentação)

---

## 1. O que ela faz

```text
  pedido em linguagem natural
  (ou transcrição de reunião)
             │
             ▼
   ┌──────────────────┐   lê o repositório alvo e decide:
   │  1. INTAKE       │   solução nova, ou feature em app existente?
   └──────────────────┘
             │
             ▼
   ┌──────────────────┐   requisitos verificáveis + as ambiguidades
   │  2. REQUISITOS   │──▶ ❓ pergunta ao humano (no máximo 5, com opções concretas)
   └──────────────────┘
             │
             ▼
   ┌──────────────────┐   work items, dependências e ondas de paralelismo
   │  3. PLANEJAMENTO │   ↻ validador determinístico + agente crítico
   └──────────────────┘
             │
             ▼
        🛑 APROVAÇÃO HUMANA  ← nada é escrito no GitHub antes daqui
             │
             ▼
   ┌──────────────────┐   labels, milestone, uma issue por work item
   │  4. PUBLICAÇÃO   │
   └──────────────────┘
             │
             ▼
   ┌──────────────────────────────────────────────────┐
   │  5. IMPLEMENTAÇÃO — onda por onda, em paralelo   │
   │                                                  │
   │   issue #12        issue #13        issue #14    │
   │      │                │                │         │
   │   worktree         worktree         worktree     │
   │   Copilot          Copilot          Copilot      │
   │      │                │                │         │
   │      ▼                ▼                ▼         │
   │   ┌────────────────────────────────────────┐     │
   │   │ GATE: format · build -warnaserror ·    │     │
   │   │ testes · vulnerabilidades · escopo ·   │     │
   │   │ varredura de segredos                  │     │
   │   └────────────────────────────────────────┘     │
   │      │ reprovou? devolve o diagnóstico ao agente │
   │      ▼ (até N tentativas)                        │
   │   revisão automatizada do diff                   │
   └──────────────────────────────────────────────────┘
             │
             ▼
   ┌──────────────────┐   push + PR com relatório de validação,
   │  6. ENTREGA      │   apontamentos da revisão e procedência
   └──────────────────┘
             │
             ▼
        🛑 O MERGE É SEMPRE HUMANO
```

**Duas decisões de design que valem mais do que o resto:**

**(a) O gate determinístico decide, não o modelo.** O agente relata o que acha que fez; o
`dotnet build -warnaserror` e o `dotnet test` estabelecem o que é verdade. Nenhum pull
request é aberto sem passar por eles.

**(b) Dois work items da mesma onda nunca declaram o mesmo arquivo.** É a regra que faz o
paralelismo funcionar, e ela é verificada por código, não por boa vontade do modelo.
Sem ela, dois agentes editam `OrderService.cs` ao mesmo tempo e você troca paralelismo por
conflito de merge.

---

## 2. Arquitetura

| Projeto | Papel |
|---|---|
| `AgentSquad.Core` | Domínio e abstrações. Zero dependência de infraestrutura. |
| `AgentSquad.Tools` | Infra determinística: processos, git, worktrees, `gh`, gate de validação, pré-requisitos. |
| `AgentSquad.Agents` | Agentes do Agent Framework sobre o Foundry, agente Copilot, orquestração. |
| `AgentSquad.Cli` | Console (`squad run`, `squad doctor`). |
| `AgentSquad.Web` | Dashboard Blazor Server: progresso ao vivo, aprovação do plano, links dos PRs. |
| `AgentSquad.AppHost` | Aspire — sobe o dashboard e dá o visualizador OpenTelemetry. |
| `AgentSquad.ServiceDefaults` | Telemetria, health checks, resiliência HTTP. |

### Os agentes

| Agente | Modelo | O que faz |
|---|---|---|
| Transcript Analyst | `gpt-5.5` | Extrai decisões, requisitos e **pendências não resolvidas** de uma ata |
| Intake Analyst | `gpt-5.4-mini` | Classifica greenfield/brownfield e descreve o repositório |
| Requirements Analyst | `gpt-5.5` | Requisitos verificáveis + perguntas para o humano |
| Architect | `gpt-5.5` | Work items, dependências, ondas |
| Plan Critic | `gpt-5.5` | Revisa o plano antes de qualquer issue existir |
| Code Reviewer | `gpt-5.3-codex` | Revisa o diff citando o identificador da regra |
| **Implementer** | **GitHub Copilot** | **Escreve o código, em worktree isolado** |

### Por que o fan-out não é um grafo de workflow

O Microsoft Agent Framework tem `Microsoft.Agents.AI.Workflows`, e ele modela bem a
metade inicial do pipeline. A onda de implementação é outro problema: concorrência
limitada, orçamento de tempo por agente, laço de reparo com teto de tentativas, e ciclo de
vida de worktree que precisa ser limpo mesmo quando um agente morre. Expressar isso como
grafo esconderia justamente o controle que o operador mais precisa ver, então é C#
explícito e testável. Está registrado em [`docs/adr/0001-orchestration-model.md`](docs/adr/0001-orchestration-model.md).

---

## 3. Pré-requisitos

| | Versão mínima | Como instalar |
|---|---|---|
| .NET SDK | 10.0.100 | <https://dotnet.microsoft.com/download/dotnet/10.0> |
| git | 2.45 | `winget install Git.Git` |
| GitHub CLI | **2.101.0** | `winget install GitHub.cli` |
| GitHub Copilot CLI | 1.0.80 | `npm install -g @github/copilot` *(opcional — o SDK traz o seu)* |
| Azure CLI | 2.60 | `winget install Microsoft.AzureCLI` |
| Assinatura Azure | — | com permissão de criar `Microsoft.CognitiveServices` |
| Assinatura GitHub Copilot | — | qualquer plano com Copilot habilitado |

**Não decore nada disso.** Rode:

```powershell
dotnet run --project src/AgentSquad.Cli -- doctor
```

Cada item reprovado vem com o comando exato que o corrige:

```text
╭───┬────────────────────────────┬─────────────────────────────────────────────╮
│ ✔ │ .NET SDK                   │ 10.0.401                                    │
│ ✔ │ GitHub CLI                 │ 2.101.0                                     │
│ ✖ │ GitHub token scopes        │ 'gist', 'read:org', 'repo'                  │
│ ✔ │ Microsoft Foundry endpoint │ https://....services.ai.azure.com/api/...   │
╰───┴────────────────────────────┴─────────────────────────────────────────────╯

✖ GitHub token scopes
  ╭───────────────────────────────────────────╮
  │ gh auth refresh -h github.com -s workflow │
  ╰───────────────────────────────────────────╯
```

E, quando quiser provar que o caminho inteiro até o modelo funciona **antes** de subir no
palco:

```powershell
dotnet run --project src/AgentSquad.Cli -- doctor --probe-models
```

```text
╭───┬───────────────┬───────────────────────┬──────────┬────────────────────────╮
│ ✔ │ gpt-5.5       │ requisitos e plano    │ 36946 ms │ Olá, TDC São Paulo!    │
│ ✔ │ gpt-5.4-mini  │ crítica e estruturado │  1628 ms │ Olá, TDC São Paulo!    │
│ ✔ │ gpt-5.3-codex │ revisão de código     │  1776 ms │ Olá, TDC São Paulo!    │
╰───┴───────────────┴───────────────────────┴──────────┴────────────────────────╯
O caminho Entra ID → Foundry → Agent Framework está funcionando de ponta a ponta.
```

---

## 4. Replicando do zero

### 4.1 Clonar e compilar

```powershell
git clone https://github.com/magoolation/tdc-sp-2026-agent-squad.git
cd tdc-sp-2026-agent-squad

dotnet restore AgentSquad.slnx
dotnet build   AgentSquad.slnx -c Release -warnaserror
dotnet test    AgentSquad.slnx --no-build -c Release
```

### 4.2 Provisionar o Microsoft Foundry

> **Antes de tudo, confira a cota.** Uma assinatura pode estar habilitada para um modelo e
> mesmo assim ter cota zero para ele — o deployment falha no *preflight*, não em runtime.
>
> ```powershell
> az cognitiveservices usage list -l eastus2 -o table
> ```

```powershell
az login
az account set --subscription "<sua-subscription>"

$rg  = "rg-agent-squad"
$loc = "eastus2"

az group create -n $rg -l $loc

$me = az ad signed-in-user show --query id -o tsv

az deployment group create `
  -g $rg `
  -f infra/main.bicep `
  -p principalId=$me `
  -p location=$loc `
  --query "properties.outputs"
```

A saída traz o que você precisa:

```json
{
  "projectEndpoint": { "value": "https://fdy....services.ai.azure.com/api/projects/agent-squad" },
  "deploymentNames": { "value": ["gpt-5.5", "gpt-5.4-mini", "gpt-5.3-codex"] }
}
```

O que o Bicep cria:

- Uma conta Foundry (`Microsoft.CognitiveServices/accounts`, kind `AIServices`) com
  **`disableLocalAuth: true`** — não existe chave para vazar.
- Um projeto Foundry.
- Três model deployments, um por papel.
- Atribuições RBAC **Foundry User** e **Foundry Project Manager**, no escopo da conta.

### 4.3 Criar o repositório alvo

```powershell
gh repo create <owner>/<repo> --private --clone
gh auth refresh -h github.com -s workflow
```

O escopo `workflow` é necessário no momento em que um agente tocar
`.github/workflows/` — o que uma entrega greenfield quase sempre faz.

### 4.4 Configurar

Segredos ficam fora do repositório (SEC-001):

```powershell
dotnet user-secrets --project src/AgentSquad.Cli set "Foundry:ProjectEndpoint" "https://<conta>.services.ai.azure.com/api/projects/agent-squad"
dotnet user-secrets --project src/AgentSquad.Cli set "GitHub:Owner"            "<owner>"
dotnet user-secrets --project src/AgentSquad.Cli set "GitHub:Repository"       "<repo>"
```

O que é seguro versionar fica em `src/AgentSquad.Cli/appsettings.json`:

```jsonc
{
  "Squad": {
    "WorkRoot": "C:\\squad",        // mantenha curto: há um teto de caminho no Windows
    "MaxParallelAgents": 4,         // 8–12 é o ponto ideal numa máquina de 32 núcleos
    "MsBuildNodesPerAgent": 4,      // agentes × nós ≈ núcleos
    "MaxRepairAttempts": 2,
    "AgentTimeout": "00:20:00",
    "RequirePlanApproval": true,    // AI-005 — desligue só para ensaio
    "OpenPullRequestsAsDraft": true
  },
  "Foundry": {
    "PlanningModel":  "gpt-5.5",
    "WorkhorseModel": "gpt-5.4-mini",
    "ReviewModel":    "gpt-5.3-codex"
  },
  "Copilot": {
    "Model": "claude-sonnet-5",     // modelo do Copilot, não do Foundry
    "ReasoningEffort": "high"
  }
}
```

### 4.5 Confirmar

```powershell
dotnet run --project src/AgentSquad.Cli -- doctor --probe-models
```

---

## 5. Executando

### Console

```powershell
# a partir de um pedido
dotnet run --project src/AgentSquad.Cli -- run `
  --request "Preciso de uma API de catálogo de produtos com busca paginada e testes de integração."

# a partir da transcrição de uma reunião
dotnet run --project src/AgentSquad.Cli -- run `
  --transcript samples/meeting-transcripts/kickoff-catalogo.md

# ensaiar sem escrever nada no GitHub
dotnet run --project src/AgentSquad.Cli -- run --request "..." --plan-only
```

| Opção | Efeito |
|---|---|
| `--request`, `-r` | O pedido em linguagem natural |
| `--transcript`, `-t` | Transcrição de reunião de levantamento |
| `--parallel`, `-p` | Agentes simultâneos |
| `--plan-only` | Para depois do plano, sem criar issues |
| `--unattended` | Usa o padrão declarado em toda decisão humana (ensaio e CI) |
| `--owner`, `--repo` | Sobrescreve o repositório alvo |

### Web, sob o Aspire

```powershell
dotnet run --project src/AgentSquad.AppHost
```

O dashboard do Aspire abre com a URL do Agent Squad e — o que interessa — o
**visualizador GenAI**: cada `invoke_agent`, `chat` e `execute_tool` com contagem de
tokens e latência, ao vivo, enquanto a plateia assiste.

A tela do Agent Squad mostra o progresso em tempo real, apresenta as perguntas do analista
de requisitos e o plano para aprovação, e lista os pull requests no fim. Console e web
consomem **o mesmo fluxo de eventos**, então nunca contam histórias diferentes.

---

## 6. A camada de personalização

Tudo que orienta os agentes é arquivo versionado, revisável em pull request. Nada está
escondido em prompt de código.

```text
AGENTS.md                            contrato de trabalho — lido pelo Copilot CLI e injetado no prompt
docs/engineering-rules.md            as regras, com identificador estável (ENG-042, SEC-004, TST-004…)
.github/
  copilot-instructions.md            instruções do repositório
  instructions/*.instructions.md     instruções por caminho de arquivo, aplicadas automaticamente
  agents/*.md                        agentes customizados: implementer, planner, reviewer, test-author
  skills/<nome>/SKILL.md             habilidades reutilizáveis
```

Confira que o Copilot CLI enxerga tudo:

```powershell
copilot instruction list   # AGENTS.md, copilot-instructions.md, as 4 por caminho
copilot skill list         # as 4 skills do projeto
copilot --agent planner    # os 4 agentes customizados
```

**As skills incluídas:**

| Skill | Para quê |
|---|---|
| `dotnet-validation` | Rodar e **interpretar** o gate: o que cada código de erro significa e qual é a correção certa |
| `worktree-hygiene` | O que um agente pode e não pode fazer com git quando há outros agentes rodando |
| `requirements-elicitation` | Separar o que foi dito do que foi suposto; quando perguntar e quando decidir |
| `github-issue-authoring` | Fatiar trabalho para agentes paralelos sem criar conflito |

**Para adaptar à sua empresa:** edite `docs/engineering-rules.md` e `AGENTS.md`. Os
identificadores das regras aparecem nos apontamentos da revisão automatizada, então trocar
a regra troca o que a fábrica cobra — sem tocar em uma linha de C#.

---

## 7. Regras da empresa, aplicadas de verdade

As regras não são um documento que ninguém lê. Elas são executáveis:

| Onde está escrita | Como é aplicada |
|---|---|
| `.editorconfig` | `dotnet format --verify-no-changes` reprova o PR |
| `Directory.Build.props` | `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, analisadores como erro |
| `.editorconfig` (CA5xxx) | Toda regra de segurança do .NET escalada para **error** |
| `Directory.Packages.props` | Central Package Management — nenhuma versão solta em `.csproj` |
| `docs/engineering-rules.md` | Citada por identificador em cada apontamento bloqueante da revisão |
| `AGENTS.md` §3 | Regras invioláveis — o agente é instruído a **recusar** o que as viola |
| `CopilotOptions.DeniedShellCommands` | A política de permissões **impede** tecnicamente, não apenas pede |

Exemplo de apontamento da revisão automatizada:

```json
{
  "rule": "SEC-004",
  "severity": "blocking",
  "file": "src/Tools/Git/GitCli.cs",
  "line": 88,
  "finding": "ProcessStartInfo.Arguments montado por concatenação com o nome do branch vindo da issue.",
  "fix": "Trocar para ArgumentList.Add(...) por argumento."
}
```

---

## 8. Modelo de segurança

Baseado no **Microsoft SDL**, no **OWASP Top 10 for LLM Applications** e no
**Responsible AI Standard**. Detalhes em [`docs/engineering-rules.md`](docs/engineering-rules.md) §5 e §6.

| Risco | Controle, concreto |
|---|---|
| **Agência excessiva** (LLM08) | O agente é autônomo dentro do worktree e impotente fora dele. `git push`, `gh pr`, `az` e afins são **negados** pela política de permissões — e negação vence qualquer aprovação, inclusive a automática. |
| **Prompt injection** (LLM01) | Transcrição, README do repositório alvo, corpo de issue e saída de ferramenta entram delimitados por `<<<UNTRUSTED … UNTRUSTED>>>`, com instrução explícita de tratar como dado. |
| **Exfiltração** (LLM06) | Busca na web restrita a uma allow-list de domínios de documentação. Arquivos com cara de credencial (`.env`, `.ssh`, `.pem`) são negados na leitura. |
| **Escape de sandbox** | Todo caminho é normalizado e comparado com separador final — `C:\wt\i42-evil` **não** satisfaz `C:\wt\i42`. Há teste para isso. |
| **Segredos** | Zero chave no repositório. Foundry com `disableLocalAuth`; autenticação por Entra ID. Varredura de segredos no diff antes do PR. |
| **Injeção de comando** | `ProcessStartInfo.ArgumentList` sempre; nunca linha de comando concatenada. Há teste que passa `a && whoami \| echo pwned` como argumento e confirma o round-trip literal. |
| **Human-in-the-loop** | Plano aprovado por humano antes de qualquer issue. **Merge sempre humano.** |
| **Auditabilidade** | Prompt, modelo, tokens, ferramentas chamadas, permissões negadas e diff ficam em `.squad/runs/<runId>/`. |
| **Fuga de custo** | Teto de tentativas, timeout por agente, timeout por execução, limite de concorrência — todos configuráveis e aplicados. |

Cada agente roda com um `COPILOT_HOME` próprio: sessões, aprovações persistidas e logs não
se misturam entre agentes concorrentes.

---

## 9. Bônus: da reunião ao pull request

```powershell
dotnet run --project src/AgentSquad.Cli -- run `
  --transcript samples/meeting-transcripts/kickoff-catalogo.md
```

A transcrição de exemplo é uma reunião de kickoff **escrita com as patologias de uma
reunião real**: um requisito que ninguém quantificou, uma divergência que ficou sem
resolução, um "obviamente" que não é óbvio, um escopo que cresceu no meio da conversa e uma
decisão tomada por quem chegou atrasado.

O valor da demonstração não é o agente resumir a reunião. É ele **separar o que foi
decidido do que só pareceu decidido**:

> **[03:10] Juliana:** Dá pra trabalhar com p95 abaixo de 300 ms?
> **[03:20] Renata:** Se for isso eu assino embaixo.
> **[03:24] Caio:** Anota como meta, não como SLA.

Três pessoas, dois entendimentos, zero reconciliação. Um resumo comum registra
"p95 < 300 ms" como requisito acordado — e erra. O analista deve listar isso como
**pendência**.

O arquivo traz, no fim, a lista do que um bom analista deveria extrair, para você conferir
ao vivo se o agente acertou.

---

## 10. O que descobrimos construindo isto

Achados reais, medidos nesta máquina. Cada um custou tempo, e cada um tem uma lição.

### `NUGET_SCRATCH` compartilhado não é ajuste fino

Os locks entre processos do NuGet vivem em `%TEMP%\NuGetScratch`, **não** na pasta de
pacotes. Com pasta de pacotes compartilhada e scratch por worker, **24 de 24** restores
concorrentes falharam — e corromperam o cache **permanentemente**: todo pacote ficou com o
marcador `.nupkg.metadata` mas sem o `.nuspec`, e restores seriais posteriores falhavam com
`NU5037` até limpar o cache na mão. Um scratch para todos: 0 falhas em 24.

### `git worktree remove` não é atômico no Windows

Quando ele não consegue apagar o diretório — e **um processo apenas tê-lo como diretório
atual já basta** — ele já apagou os arquivos rastreados e já desregistrou o worktree.
Retry dá "is not a working tree" e `prune` não acha nada. A recuperação é sistema de
arquivos. O gerenciador aqui derruba os build servers, tenta remover, e então apaga com
backoff exponencial.

### Nós do MSBuild seguram analisadores por 15 minutos

Medido com PIDs estáveis: três nós e um `VBCSCompiler` continuavam vivos **17 minutos**
depois do build, e só morriam com `dotnet build-server shutdown` explícito. São eles que
bloqueiam a remoção do worktree.

### Criar worktree com branch de tracking corre risco de race

`git worktree add -b X <path> origin/main` escreve `branch.X.remote` no `.git/config`
**compartilhado**. Com 16 criações simultâneas, ~4% falharam deixando o branch criado e a
seção de config pela metade. Com `--no-track`: 0 falhas. O upstream é definido depois, no
push.

### `az` no Windows é um `.cmd`

`Process.Start` com `UseShellExecute=false` não executa `.cmd`, e o sintoma é um
pré-requisito funcionando ser reportado como ausente. A correção é `cmd.exe /d /c` —
**sem `/s`**: com `/s`, o `cmd` remove as aspas externas e um caminho com espaços
(`C:\Program Files\...`) se quebra. Verificado empiricamente.

### Modelos parceiros são compra de Marketplace

Deployment de Anthropic/xAI/Mistral/Cohere exige `modelProviderData` (setor, organização,
país) — indocumentado na maioria dos exemplos — **e** um método de pagamento válido na
assinatura. Assinaturas internas, patrocinadas e de crédito falham com
`Marketplace Subscription purchase eligibility check failed`. Modelos OpenAI são vendidos
direto pela Azure e não têm esse requisito.

### `dotnet test` pode reportar "Zero tests ran" para uma suíte que passa

No SDK .NET 10 com Microsoft.Testing.Platform, `dotnet test` reporta zero testes para uma
suíte que roda perfeitamente pelo executável. Reproduzido com projeto xUnit v3 mínimo e
limpo — é defeito de SDK. Aceitar isso em silêncio seria o pior desfecho possível: o gate
aprovaria um PR cujos testes nunca rodaram. Por isso o gate trata zero testes como
suspeito e **re-executa cada projeto de teste diretamente**.

### O `dotnet.config` morreu no RC2

A configuração do runner de teste mudou de `dotnet.config` para `global.json`:

```json
{ "test": { "runner": "Microsoft.Testing.Platform" } }
```

### Nomes que mudaram e quebram código de blog post

| Era | É |
|---|---|
| Azure AI Foundry | **Microsoft Foundry** |
| papel "Azure AI User" | **"Foundry User"** (use o **GUID**, não o nome) |
| `Microsoft.Agents.AI.AzureAI` | **`Microsoft.Agents.AI.Foundry`** |
| `CreateAIAgent(...)` | **`AsAIAgent(...)`** |
| `AgentThread` / `GetNewThread()` | **`AgentSession` / `CreateSessionAsync()`** |
| `Aspire.Hosting.Azure.AIFoundry` | **`Aspire.Hosting.Foundry`** |
| `AddAzureAIFoundry(...)` | **`AddFoundry(...)`** |
| `.NET Aspire` | **Aspire** (9.5 → **13.x**, sem 10/11/12) |
| `ReflectingExecutor<T>` | `Executor<TIn, TOut>` *(o antigo está obsoleto)* |

E não existe resource provider `Microsoft.Foundry/*`: o tipo ARM continua sendo
`Microsoft.CognitiveServices/accounts` com kind `AIServices`.

---

## 11. Solução de problemas

| Sintoma | Causa | Correção |
|---|---|---|
| `doctor` reprova "GitHub token scopes" | Falta o escopo `workflow` | `gh auth refresh -h github.com -s workflow` |
| `InsufficientQuota` no deployment | Cota zero para o modelo nessa região | `az cognitiveservices usage list -l <região> -o table` e escolha outro |
| `Marketplace ... no valid payment method` | Modelo parceiro em assinatura sem meio de pagamento | Use modelos OpenAI (§10) |
| `InvalidModelProviderData` | Modelo parceiro sem atestação | Preencha `providerIndustry`, `providerOrganizationName`, `providerCountryCode` |
| 403 na primeira chamada ao Foundry | RBAC ainda propagando | Aguarde alguns minutos; confirme o papel **Foundry User** no escopo da conta |
| `dotnet test` diz "Zero tests ran" | Defeito do SDK (§10) | `dotnet run --project tests/<Projeto>` — o gate já trata isso |
| Worktree não some | Build server segurando handles | `dotnet build-server shutdown`, depois apague |
| `NU5037` em restore | Cache corrompido por scratch dividido | `dotnet nuget locals all --clear` e pin em `NUGET_SCRATCH` |
| Caminho longo no build do agente | Raiz de trabalho comprida demais | Encurte `Squad:WorkRoot` (`C:\squad`) |
| Agente reprova no gate repetidamente | Critério de aceite ambíguo | Leia o `AGENT_REPORT`; quase sempre o plano é que estava vago |

Stack trace completo: defina `SQUAD_DEBUG=1`.

---

## 12. Roteiro da apresentação

| Min | O quê | Comando |
|---|---|---|
| 0–3 | O problema: paralelizar agentes sem que eles briguem | — |
| 3–6 | Pré-requisitos, com o diagnóstico útil | `squad doctor --probe-models` |
| 6–9 | Camada de personalização: arquivo, não prompt escondido | `copilot instruction list` · `copilot skill list` |
| 9–14 | **Reunião → requisitos**, com as pendências que o resumo comum perde | `squad run --transcript samples/...` |
| 14–18 | Perguntas ao humano, com o impacto de cada opção | *(na tela)* |
| 18–22 | Plano, ondas, e o validador determinístico pegando conflito | *(na tela)* |
| 22–24 | Aprovação humana → issues no GitHub | *(navegador)* |
| 24–34 | Agentes em paralelo, gate ao vivo, dashboard do Aspire | *(Aspire + dashboard)* |
| 34–38 | Pull requests, com relatório de validação e procedência | *(navegador)* |
| 38–42 | Os achados do §10 — a parte que ninguém mais vai contar | *(slides)* |
| 42–45 | Perguntas | — |

**Plano B:** se a rede cair, `--plan-only` roda tudo até o plano sem tocar no GitHub, e as
issues e PRs de um ensaio anterior continuam abertos para navegar.

---

## Créditos e licença

Inspirado na ideia de times de agentes apresentada em
[*Building agent teams with Agent Framework, GitHub Copilot CLI, and Squad*](https://devblogs.microsoft.com/agent-framework/building-agent-teams-with-agent-framework-github-copilot-cli-and-squad/)
(Microsoft DevBlogs). Este repositório é uma implementação independente, com arquitetura,
código e decisões próprios.

MIT. Veja [`LICENSE`](LICENSE).
