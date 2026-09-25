<!--
  Pull requests opened by the factory fill this in automatically.
  A human opening one by hand fills it in the same way — the bar is identical (PRC-004).
-->

Closes #

## O que mudou

<!-- Uma frase: qual comportamento observável passa a existir. -->

## Por quê

<!-- O motivo, não a descrição do diff. O diff já está aí. -->

## Como verificar

<!-- Como um revisor confirma que funciona, sem ter que adivinhar. -->

```powershell
dotnet build AgentSquad.slnx -c Release -warnaserror
dotnet test  AgentSquad.slnx --no-build -c Release
```

## Checklist

- [ ] `dotnet format --verify-no-changes` passa
- [ ] `dotnet build -warnaserror` passa com zero warnings
- [ ] `dotnet test` passa
- [ ] Comportamento novo tem teste; correção de bug começou por um teste que falhava
- [ ] Nenhum segredo, token ou connection string no diff
- [ ] Nenhum analisador suprimido para fazer o build passar
- [ ] Pacote novo (se houver) registrado em `Directory.Packages.props` e justificado abaixo
- [ ] Documentação atualizada no mesmo PR, quando o comportamento mudou

## Atenção do revisor

<!--
  O que merece um olhar cuidadoso. Seja honesto: "não testei o caminho de cancelamento"
  vale mais do que silêncio.
-->

## Pacotes novos

<!-- Se adicionou dependência: licença, manutenção, tamanho, e qual alternativa da BCL foi considerada (BLD-006). -->
