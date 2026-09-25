# Runbook da demonstração

Checklist operacional para apresentar o Agent Squad ao vivo. Escrito para ser seguido com
a plateia esperando.

---

## T-24h — a véspera

```powershell
dotnet run --project src/AgentSquad.Cli -- doctor --probe-models
```

Tudo verde. Se o `probe-models` falhar, você tem um dia para resolver; às 9h do evento,
não tem.

**Confira a cota.** Uma assinatura pode estar habilitada para um modelo e ainda assim ter
cota zero:

```powershell
az cognitiveservices usage list -l eastus2 -o table
az cognitiveservices account deployment list -n <conta> -g <rg> -o table
```

**Faça um ensaio completo, de verdade**, contra um repositório descartável:

```powershell
gh repo create <owner>/squad-ensaio --private
dotnet run --project src/AgentSquad.Cli -- run `
  --request "Adicione uma calculadora de frete com faixas de CEP e testes." `
  --owner <owner> --repo squad-ensaio
```

Cronometre. O tempo do ensaio é o tempo do palco, mais o que a rede do evento cobrar.

**Deixe um ensaio anterior aberto.** Issues e PRs de um ensaio bem-sucedido são o seu
plano B se a rede cair no meio.

---

## T-1h — antes de subir

```powershell
# 1. Um doctor limpo, já com a saída na tela para a plateia ver
dotnet run --project src/AgentSquad.Cli -- doctor --probe-models

# 2. Limpe worktrees e cache de execuções anteriores
Remove-Item -Recurse -Force C:\squad\wt\*  -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force C:\squad\runs\* -ErrorAction SilentlyContinue

# 3. Derrube build servers (eles seguram handles por 15 minutos)
dotnet build-server shutdown

# 4. Aqueça o build — ninguém quer ver restore no palco
dotnet build AgentSquad.slnx -c Release
```

**Ambiente da sessão:**

```powershell
$env:SQUAD_DEBUG = $null          # sem stack trace na tela
$env:NO_COLOR    = $null          # cores ligadas
```

**Terminal:** fonte grande (18pt+), tema escuro, janela larga — as tabelas do Spectre
quebram feio em janela estreita.

**Abas do navegador, já abertas:**
1. O repositório alvo, na aba Issues
2. O portal do Foundry (<https://ai.azure.com>), no projeto
3. Uma aba em branco para o dashboard do Aspire

---

## Durante

### O que dizer enquanto espera

O agente de planejamento leva de 30 a 60 segundos. **Isso é tempo de fala, não tempo
morto.** Use para explicar:

- Por que dois itens da mesma onda nunca podem tocar o mesmo arquivo.
- Por que o gate determinístico decide, e não o modelo.
- Por que o merge continua sendo humano.

### Os momentos que valem a pena

| Momento | O que apontar |
|---|---|
| Perguntas ao humano | Cada opção mostra o **impacto no plano**, não só um rótulo |
| Plano na tela | As ondas, e o validador determinístico pegando conflito de arquivo |
| Aprovação | "Até aqui, nada foi escrito no GitHub" |
| Issues aparecendo | Alterne para o navegador; elas estão lá de verdade |
| Agentes em paralelo | O dashboard do Aspire, com os spans GenAI ao vivo |
| Gate reprovando | **Se acontecer, comemore.** É a parte mais importante da demo |
| Pull requests | O relatório de validação e o bloco de procedência no corpo |

### Se o gate reprovar um agente

Isso é um recurso, não uma falha. O roteiro:

> "Repararam? O agente disse que terminou. O gate discordou. O diagnóstico do compilador
> volta para ele, ele corrige, e roda de novo. É exatamente por isso que o gate existe:
> num sistema autônomo, a única coisa em que você pode confiar é no que um programa
> verificou."

---

## Quando der errado

| Sintoma | O que fazer, ao vivo |
|---|---|
| Rede caiu | `--plan-only` roda tudo até o plano sem tocar no GitHub. Depois navegue no ensaio anterior. |
| Foundry com 429 | Baixe `MaxParallelAgents` para 2, ou aumente a capacidade do deployment |
| Um agente travou | Ele estoura o timeout e o run segue. Diga isso em voz alta — é o controle funcionando |
| Copilot sem autenticação | `copilot login`, ou exporte `GH_TOKEN` |
| Worktree não some | `dotnet build-server shutdown` e siga; a limpeza acontece no fim |
| Plano veio ruim | Use **Revisar** com um comentário. Mostrar o arquiteto corrigindo é ótima demo |

**Regra de ouro:** se algo quebrar, explique o que quebrou e por quê. Uma plateia técnica
perdoa uma falha explicada e desconfia de uma demo perfeita demais.

---

## Depois

```powershell
# Limpar as issues e PRs do ensaio, se quiser reusar o repositório
gh pr list  --repo <owner>/<repo> --json number --jq '.[].number' | ForEach-Object { gh pr close $_ --repo <owner>/<repo> --delete-branch }
gh issue list --repo <owner>/<repo> --json number --jq '.[].number' | ForEach-Object { gh issue close $_ --repo <owner>/<repo> }

# Derrubar a infraestrutura, se não for usar de novo
az group delete -n rg-tdcsp2026-agent-squad --yes --no-wait
```

O journal de cada execução fica em `C:\squad\runs\<runId>\` — `events.jsonl` e os
transcripts por issue. Vale guardar: é o material de follow-up e a evidência de
auditabilidade.

---

## Partner models — leia antes de trocar um modelo

Modelos parceiros (Anthropic, xAI, Mistral, Cohere) no Foundry são compras de Marketplace.
São **dois** requisitos além da cota:

1. **`modelProviderData`** no deployment — setor, organização, país. Sem isso:
   `InvalidModelProviderData` no preflight. O `infra/main.bicep` já envia para qualquer
   formato que não seja `OpenAI`.
2. **Método de pagamento válido na assinatura.** Assinaturas internas, patrocinadas e de
   crédito falham com `Marketplace Subscription purchase eligibility check failed`.

Modelos OpenAI são vendidos direto pela Azure e não têm nenhum dos dois requisitos. É por
isso que a configuração padrão usa `gpt-5.5`, `gpt-5.4-mini` e `gpt-5.3-codex`.
