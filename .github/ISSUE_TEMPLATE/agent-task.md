---
name: Tarefa para agente
about: Uma unidade de trabalho que um agente autônomo consegue implementar sozinho
title: ''
labels: agent-task
assignees: ''
---

<!--
  Um humano preenche lacunas conversando. Um agente autônomo, não — ele tem apenas esta
  issue, o AGENTS.md, as regras de engenharia e o repositório. Se algo importante só
  existe na sua cabeça, ele vai inventar.
-->

## Objetivo

<!-- Uma frase: qual comportamento observável passa a existir. -->

## Contexto

<!--
  Por que isso é necessário. Em repositório existente, cite os arquivos que estabelecem
  o padrão a ser seguido.
-->

## Escopo — arquivos previstos

| Arquivo | Ação |
|---|---|
| `src/.../Exemplo.cs` | criar |
| `tests/.../ExemploTests.cs` | criar |

**Fora de escopo:** <!-- o que explicitamente NÃO deve ser tocado -->

## Critérios de aceite

<!-- Precisam ser decidíveis por format + build + test. Se você não imagina o teste, ainda está vago. -->

- [ ] Dado `<contexto>`, quando `<ação>`, então `<resultado observável>`
- [ ] Dado `<contexto de erro>`, quando `<ação>`, então `<erro tratado>`
- [ ] `dotnet format --verify-no-changes`, `dotnet build -warnaserror` e `dotnet test` verdes

## Notas de implementação

<!-- Restrições e armadilhas conhecidas. Não escreva o código aqui. -->

<!-- Bloco lido pelo orquestrador para montar o grafo de dependências e checar conflito de arquivos. -->
<!-- squad:meta
wave: 1
dependsOn: []
files:
  - src/.../Exemplo.cs
  - tests/.../ExemploTests.cs
-->
