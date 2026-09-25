---
name: worktree-hygiene
description: Regras de isolamento para agentes que trabalham em git worktrees paralelos - o que pode e o que não pode ser tocado, como evitar conflito entre agentes concorrentes e como entregar o branch. Use antes de qualquer operação de git.
---

# Skill: higiene de worktree

## O modelo de isolamento

O orquestrador cria, para **cada** issue, um worktree independente:

```text
<repo>/                         ← clone principal, branch main. NÃO TOQUE.
<repo>/.worktrees/issue-42/     ← seu worktree, branch agent/issue-42-<slug>
<repo>/.worktrees/issue-43/     ← worktree de OUTRO agente, rodando agora
```

Todos os worktrees compartilham **o mesmo diretório `.git`**. Isso significa que uma
operação global de git feita por você **corrompe o trabalho dos outros agentes que
estão rodando ao mesmo tempo**.

## Proibido (sem exceção)

```text
git checkout / git switch          ← muda o HEAD; quebra o pareamento worktree↔branch
git worktree add / remove / prune  ← só o orquestrador gerencia worktrees
git merge / rebase / cherry-pick   ← integração é decisão do orquestrador e do humano
git reset --hard                   ← destrói trabalho
git push --force / --force-with-lease
git branch -D / git push --delete
git config --global ...            ← afeta todos os worktrees
git gc / git prune                 ← pode remover objetos em uso por outro worktree
git stash                          ← a stash é global, não do worktree
```

## Permitido

```text
git status
git diff / git diff --staged
git add <caminhos específicos dentro do escopo da issue>
git commit -m "..."
git log
git show
```

## Escopo de arquivos

1. Antes de editar, confirme que o arquivo está no escopo declarado da sua issue.
2. **Arquivos compartilhados são zona de conflito.** Estes exigem cuidado especial
   porque outros agentes podem estar mexendo neles agora:
   - `Directory.Packages.props`
   - `Directory.Build.props`
   - `AgentSquad.slnx`
   - `.editorconfig`
   - `README.md`

   Se precisar alterar um deles, faça a **menor** alteração aditiva possível
   (uma linha nova, nunca reordenação/reformatação do arquivo inteiro) e declare isso
   em `risks` no seu relatório. Reformatar um arquivo compartilhado garante conflito.
3. **Nunca** altere arquivos sob `.worktrees/` de outro agente.
4. **Nunca** altere arquivos fora da raiz do seu worktree.
5. Arquivos gerados (`bin/`, `obj/`, `.squad/`) nunca são commitados.

## Antes de commitar

```powershell
git status --short           # nada inesperado?
git diff --staged            # revise o que vai entrar, linha a linha
```

Perguntas obrigatórias:

- Todo arquivo alterado está no escopo da issue? Se não → remova do stage.
- Existe segredo, token, caminho absoluto da minha máquina, ou `TODO` órfão no diff?
- Existe arquivo binário ou gerado no diff?
- O diff tem menos de ~400 linhas úteis? Se não, a issue provavelmente era grande
  demais — reporte isso em `risks`.

## Commit

```powershell
git add <arquivos>
git commit -m "feat(planning): add dependency-aware wave planner" `
           -m "Ordena work items por dependência para que agentes paralelos nunca disputem o mesmo arquivo." `
           -m "Refs: #42"
```

Um commit lógico por mudança coesa. Vários commits pequenos são preferíveis a um
commit gigante.

## Entrega

**Você não faz push e não abre o PR.** Ao terminar o commit, apenas emita o
`AGENT_REPORT`. O orquestrador faz `git push -u origin <branch>` e `gh pr create`
após rodar o gate de validação independentemente e após a revisão automatizada.

## Se der conflito

Você não deve ver conflito — cada worktree parte de um `main` limpo. Se vir:

1. **Pare.** Não resolva.
2. Reporte `status: "blocked"` com `blockedReason` descrevendo o conflito.

Conflito significa que o plano de paralelização estava errado, e isso é informação
valiosa para o orquestrador — não um problema para você contornar.
