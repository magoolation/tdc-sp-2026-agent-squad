---
name: requirements-elicitation
description: Como transformar um pedido vago (ou a transcrição de uma reunião) em requisitos verificáveis, separando o que foi dito do que foi suposto e produzindo uma lista curta de ambiguidades para perguntar ao humano. Use na fase de intake, antes de qualquer planejamento.
---

# Skill: elicitação de requisitos

## Objetivo

Sair de "quero um sistema de checkout" e chegar em requisitos que um agente
implementador consiga verificar sozinho — **sem inventar escopo**.

## Princípio número um

> Separe rigorosamente **o que foi dito**, **o que foi decidido** e **o que você supôs**.

Toda suposição vira uma pergunta ou uma decisão explícita registrada. Requisito
inventado silenciosamente é a principal causa de retrabalho numa fábrica autônoma.

## Passo 1 — Classificar o pedido

| Sinal | Classificação |
|---|---|
| Não existe repositório, ou existe e está vazio | `Greenfield` |
| Existe repositório com código e o pedido cita comportamento existente | `Brownfield` |
| Existe repositório mas o pedido é um módulo isolado e novo | `Brownfield-NewModule` |

Em `Brownfield`, **leia o código antes de perguntar qualquer coisa**: stack, versão do
framework, padrões de camada, estratégia de teste, estilo de persistência, como a
autenticação é feita hoje. Metade das "ambiguidades" some depois de ler o repositório —
e perguntar ao humano algo que estava no código é desperdício do tempo dele.

## Passo 2 — Extrair requisitos

Para cada requisito, produza:

```json
{
  "id": "RF-01",
  "kind": "functional | nonfunctional | constraint | assumption",
  "statement": "O sistema deve <comportamento observável>",
  "rationale": "por que isso importa para o usuário",
  "source": "transcript:14:32 | user-request | inferred-from-code:src/Foo.cs",
  "confidence": "stated | implied | assumed",
  "acceptanceCriteria": [
    "Dado <contexto>, quando <ação>, então <resultado observável>"
  ]
}
```

Regras:

- `statement` descreve **comportamento observável**, não implementação.
  ❌ "usar Redis para cache" · ✅ "consultas repetidas devem responder em < 100 ms"
  (a menos que a tecnologia seja uma **restrição** declarada — aí é `kind: constraint`).
- `acceptanceCriteria` precisa ser **testável por máquina**. Se você não consegue
  imaginar o teste automatizado, o critério ainda está vago.
- `source` sempre rastreável. Em transcrição, cite o timestamp/falante.
- `confidence: "assumed"` obriga a gerar uma pergunta no Passo 4.

## Passo 3 — Requisitos não funcionais que quase sempre faltam

Percorra esta lista **toda vez**. Se o pedido não disser, é ambiguidade ou você
registra um padrão explícito:

- **Volume e desempenho:** quantos usuários/requisições/registros? qual latência aceitável?
- **Autenticação e autorização:** quem acessa? que papéis? SSO? multi-tenant?
- **Dados:** o que é persistido, por quanto tempo, onde, tem PII/LGPD?
- **Disponibilidade:** SLA? tolera indisponibilidade? precisa de retry/idempotência?
- **Integrações:** que sistemas externos? síncrono ou assíncrono? quem é a fonte da verdade?
- **Observabilidade:** o que precisa ser medido e alertado?
- **Implantação:** onde roda? qual ambiente? quem opera?
- **Compatibilidade:** quebra contrato existente? precisa migrar dados?
- **Acessibilidade e idioma:** WCAG? i18n?
- **Custo:** existe teto de gasto (infra ou tokens)?

## Passo 4 — Ambiguidades (o que perguntar ao humano)

**Máximo de 5 perguntas por rodada.** O humano tem paciência finita; uma palestra, mais
ainda. Priorize por *blast radius*: pergunte primeiro o que muda a arquitetura.

Critério para virar pergunta:

1. A resposta muda o **plano de execução** (não só um detalhe de implementação). **E**
2. Você não consegue responder lendo o repositório ou a transcrição. **E**
3. Escolher errado custa caro para desfazer.

Se falhar em qualquer um dos três: **decida você**, registre como
`kind: "assumption"` com `confidence: "assumed"`, e liste a decisão no plano sob
"Decisões tomadas por padrão" para o humano poder contestar.

Formato da pergunta — sempre com opções concretas, nunca em aberto:

```json
{
  "id": "Q-01",
  "question": "Os pedidos precisam sobreviver a uma queda do serviço antes da confirmação do pagamento?",
  "why": "Define se usamos outbox + fila (mais 3 issues) ou chamada síncrona simples.",
  "options": [
    { "label": "Sim, durabilidade garantida", "impact": "Outbox + fila. +3 issues, +1 dia." },
    { "label": "Não, pode perder em falha", "impact": "Chamada síncrona. Plano menor." }
  ],
  "defaultIfUnanswered": "Sim, durabilidade garantida",
  "blocking": true
}
```

- `blocking: true` interrompe o fluxo. Use com parcimônia — no máximo 2 por rodada.
- `defaultIfUnanswered` é **obrigatório**: o sistema precisa poder seguir sozinho.

## Passo 5 — Fechamento

Encerre quando:

- Todo requisito `functional` tem pelo menos um critério de aceite testável, **e**
- Nenhuma ambiguidade `blocking` está aberta, **e**
- A lista de suposições está escrita e visível para o humano.

## Anti-padrões

| Anti-padrão | Por quê é ruim |
|---|---|
| Perguntar 15 coisas de uma vez | O humano responde mal ou desiste |
| Perguntar o que está no código | Desperdiça o tempo dele e sinaliza que você não leu |
| Aceitar "faça do jeito padrão" sem registrar qual é o padrão | Some a rastreabilidade |
| Transformar preferência de implementação em requisito | Amarra o implementador sem motivo |
| Requisito sem critério de aceite | O agente implementador não sabe quando terminou |
| Escopo que cresce sozinho ("e já que estamos aqui...") | Quebra a estimativa e o paralelismo |
