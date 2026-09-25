---
applyTo: "infra/**"
description: "Bicep e provisionamento do Microsoft Foundry: segurança, nomeação, RBAC."
---

# Infraestrutura (Bicep / azd)

## Princípios

- **Tudo como código.** Nenhum recurso criado à mão no portal entra na demo.
- **Sem chaves.** `disableLocalAuth: true` onde o serviço suportar; acesso por
  **Microsoft Entra ID** com RBAC no escopo do recurso.
- **Menor privilégio.** Atribua o papel mínimo, no recurso, nunca na subscription.
- Idempotente: `azd up` duas vezes não pode quebrar nem duplicar.

## Convenções

- `targetScope = 'resourceGroup'` nos módulos; `subscription` só no `main.bicep`.
- Nome de recurso derivado de `uniqueString(subscription().id, environmentName, location)`
  com um `abbrs` central — nunca nome fixo que colida entre execuções.
- Toda saída sensível marcada com `@secure()`; nenhuma chave em `output`.
- Tags obrigatórias em todo recurso: `azd-env-name`, `project`, `owner`, `costCenter`.
- `@description()` em **todo** parâmetro e output.
- Parâmetros com `@allowed()` e valor padrão seguro.

## Modelos do Foundry

- Deployments declarados como recurso filho, com `sku.name` e `sku.capacity` explícitos.
- Deployments de modelo são criados **em série** (`@batchSize(1)`) — o serviço rejeita
  criação concorrente no mesmo account.
- A capacidade (TPM) é parâmetro, não literal: a demo precisa caber na quota disponível.

## Revisão obrigatória antes de PR

```powershell
az bicep build --file infra/main.bicep      # compila
az bicep lint  --file infra/main.bicep      # linter
azd provision --preview                     # what-if
```

Nenhum warning do linter do Bicep pode ficar sem tratamento.
