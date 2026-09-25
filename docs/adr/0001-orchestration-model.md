# ADR 0001 — Grafo de workflow para o planejamento, C# explícito para o paralelismo

- **Status:** aceita
- **Data:** 2026-09-25
- **Decisores:** time do Agent Squad

## Contexto

O Microsoft Agent Framework oferece `Microsoft.Agents.AI.Workflows`: um modelo de grafo com
executores, arestas, fan-out, fan-in, checkpointing e portas de requisição para
human-in-the-loop. É a abstração que o framework recomenda para orquestração.

O pipeline da fábrica tem duas metades com naturezas bem diferentes.

**A primeira metade** — intake → requisitos → esclarecimento → plano → crítica → aprovação
— é uma sequência com ramificações condicionais e uma pausa para o humano. É exatamente o
que um grafo de workflow modela bem.

**A segunda metade** — a onda de implementação — precisa de:

- concorrência limitada, com o teto vindo de configuração;
- orçamento de tempo por agente, com terminação da árvore de processos no estouro;
- laço de reparo com teto de tentativas, alimentado pelo diagnóstico do gate;
- ciclo de vida de `git worktree` que precisa ser limpo mesmo quando um agente morre,
  incluindo derrubar build servers antes da remoção;
- ondas sequenciais em que a onda N+1 só começa depois que a N inteira terminou.

## Decisão

**Usar o modelo de grafo onde ele esclarece, e C# explícito onde ele esconderia.**

A implementação atual expressa todo o pipeline como C# explícito em `SquadOrchestrator`,
com `Parallel.ForEachAsync` e `MaxDegreeOfParallelism` para a onda. O framework continua
sendo usado onde entrega valor real: `AIAgent`, `AgentSession`, saída estruturada com
schema, tools, middleware e a instrumentação OpenTelemetry — inclusive para o agente
Copilot, que é exposto como um `AIAgent` como qualquer outro.

## Justificativa

1. **O controle operacional é o produto.** Quem opera a fábrica precisa ver e ajustar
   concorrência, timeouts e tentativas. Num grafo, essas decisões ficam distribuídas entre
   opções de executor, configuração de aresta e política de runtime. Em C#, estão num
   lugar só, com nome e comentário.

2. **Limpeza determinística.** O worktree precisa desaparecer mesmo quando o agente
   trava, estoura o tempo ou morre. Um `try`/`finally` diz isso de forma que qualquer
   pessoa lê. A mesma garantia num grafo depende de entender o ciclo de vida do executor.

3. **Testabilidade.** A onda é testável com uma implementação falsa de `ICodingAgent`, sem
   nenhuma infraestrutura de runtime de workflow.

4. **Armadilhas verificadas do modelo de grafo em 1.22.** Ao avaliar, encontramos várias
   que teríamos que contornar de qualquer forma: o fan-in entrega mensagens
   individualmente e não como lista; um `Workflow` tem dono único e não pode ser executado
   em paralelo; executores com agente exigem um `TurnToken` explícito ou nada acontece; e
   executores precisam declarar seus tipos de saída ou lançam em runtime. Nenhuma é
   impeditiva — mas somadas, nesta metade do pipeline, custam mais clareza do que compram.

5. **O framework continua sendo o framework.** A escolha é sobre o modelo de orquestração,
   não sobre usar ou não o Agent Framework. Ele está em todo agente da solução.

## Consequências

**Positivas**

- Concorrência, timeouts, tentativas e limpeza ficam visíveis em um arquivo.
- A onda é testável sem infraestrutura.
- O fluxo lê como o diagrama do README.

**Negativas**

- Não há checkpoint automático: uma execução interrompida recomeça do início.
  Mitigado pelo journal de eventos em `.squad/runs/<runId>/`, que preserva o histórico, e
  pelas issues, que já estão no GitHub.
- Não aproveitamos a visualização de workflow do DevUI.
- Quem conhece o framework pode esperar um grafo aqui — daí este ADR existir.

**Como reconsiderar**

Vale revisitar se: o framework passar a expor concorrência limitada e orçamento por nó de
forma declarativa; checkpoint e retomada virarem requisito real de negócio; ou o pipeline
crescer a ponto de a sequência explícita ficar difícil de seguir.

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Tudo em `WorkflowBuilder` | Esconderia justamente o controle operacional que o operador precisa |
| `AgentWorkflowBuilder.BuildConcurrent` | Sem limite de concorrência, sem orçamento por agente, sem laço de reparo |
| Orquestração Magentic | O plano precisa de aprovação humana e verificação determinística; um manager de LLM decidindo os próximos passos é o oposto disso |
| Fila externa (Durable Functions) | Infraestrutura desproporcional para uma execução de uma máquina |
