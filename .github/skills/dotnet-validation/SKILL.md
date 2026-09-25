---
name: dotnet-validation
description: Executa e interpreta o gate de validação .NET (restore, format, build com warnings-as-errors, testes, pacotes vulneráveis). Use SEMPRE antes de commitar ou abrir PR, e sempre que precisar diagnosticar por que o gate reprovou.
---

# Skill: gate de validação .NET

## Quando usar

- Antes de qualquer commit.
- Depois de qualquer alteração em `.cs`, `.csproj`, `.props`, `.targets` ou `.editorconfig`.
- Quando o orquestrador devolver um `ValidationReport` com falha e pedir reparo.

## Sequência canônica

Execute na raiz do worktree, **nesta ordem**. Pare no primeiro erro e corrija antes de seguir.

```powershell
# 1. Restaurar (falha cedo em pacote faltando / versão divergente)
dotnet restore AgentSquad.slnx

# 2. Formatação — o gate usa --verify-no-changes; aqui corrigimos primeiro
dotnet format AgentSquad.slnx --severity info
dotnet format AgentSquad.slnx --verify-no-changes --severity info

# 3. Build com zero tolerância a warning
dotnet build AgentSquad.slnx --no-restore -c Release -warnaserror

# 4. Testes
dotnet test AgentSquad.slnx --no-build -c Release

# 5. Supply chain
dotnet list AgentSquad.slnx package --vulnerable --include-transitive
dotnet list AgentSquad.slnx package --deprecated
```

## Como interpretar cada falha

### `dotnet restore` falhou

| Sintoma | Causa provável | Correção |
|---|---|---|
| `NU1101 Unable to find package X` | pacote não existe ou fonte errada | confira o id em nuget.org; não invente pacote |
| `NU1008 Projects that use CPM must not specify version` | `Version=` no `PackageReference` | remova a versão do `.csproj` e declare `PackageVersion` em `Directory.Packages.props` |
| `NU1010 PackageReference ... no corresponding PackageVersion` | esqueceu de registrar no CPM | adicione `<PackageVersion Include="X" Version="..." />` |
| `NU1605 Detected package downgrade` | conflito transitivo | alinhe a versão no `Directory.Packages.props` |

### `dotnet format --verify-no-changes` falhou

Ele lista `arquivo(linha,coluna): error WHITESPACE/IDE0055`. **Não edite à mão:**
rode `dotnet format AgentSquad.slnx --severity info` e commite o resultado.
Se o format continuar mudando o arquivo, o `.editorconfig` é a autoridade — ajuste o
seu código, não o `.editorconfig`.

### `dotnet build -warnaserror` falhou

Trate o warning, **nunca** suprima. Mapeamento dos mais comuns aqui:

| Código | Significado | Correção correta |
|---|---|---|
| `CS8618` | campo/propriedade não-nulo não inicializado | `required`, valor padrão, ou torne nulável de verdade |
| `CS8602` | desreferência de possível nulo | verifique antes; não use `!` |
| `CS1998` | `async` sem `await` | remova o `async` e devolva `Task.FromResult`/`ValueTask` |
| `CS4014` | `Task` não aguardada | `await`, ou atribua a `_` com comentário justificando |
| `IDE0005` | `using` desnecessário | remova |
| `CA2016` | `CancellationToken` não repassado | propague o token |
| `CA1068` | `CancellationToken` fora da última posição | reordene os parâmetros |
| `CA2007` | `ConfigureAwait` | **desligado** neste repositório; se aparecer, o `.editorconfig` foi alterado indevidamente |
| `CA5xxx` | regra de **segurança** | **nunca** suprima. Corrija o código. Veja `docs/engineering-rules.md` §5 |

### `dotnet test` falhou

1. Leia a mensagem da asserção, não só o nome do teste.
2. Decida: o **teste** está errado ou o **código** está errado? Se o teste descreve o
   critério de aceite da issue, o código está errado.
3. **Nunca** conserte um teste enfraquecendo a asserção, comentando-o ou marcando `Skip`.
4. Teste instável (*flaky*) é bug: quase sempre é tempo real, ordenação ou estado
   compartilhado. Veja `.github/instructions/tests.instructions.md` §3.

### `--vulnerable` retornou algo

- `High`/`Critical`: bloqueante. Suba a versão no `Directory.Packages.props`.
- Se não houver versão corrigida, **pare e reporte** — não ignore.

## Saída esperada

Ao terminar, registre no seu relatório:

```json
"validation": { "format": "pass", "build": "pass", "test": "pass", "vulnerable": "pass" }
```

Se qualquer item for `fail` após 3 tentativas de correção, o status geral é `blocked`
e você deve explicar em `blockedReason` exatamente qual diagnóstico não conseguiu resolver.
