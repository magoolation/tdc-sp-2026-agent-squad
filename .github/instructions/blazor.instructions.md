---
applyTo: "src/AgentSquad.Web/**"
description: "Blazor Server: componentes, streaming de eventos de execução, segurança web."
---

# Blazor Server — AgentSquad.Web

## Modelo de render

- **Interactive Server** é o padrão. Componentes estáticos só para páginas sem interação.
- Nada de `@rendermode InteractiveAuto`/`WebAssembly` — a UI depende de serviços do servidor.

## Componentes

- Um componente por arquivo `.razor`; code-behind em `.razor.cs` quando passar de ~40
  linhas de C#. CSS isolado em `.razor.css`.
- Parâmetros `[Parameter]` são **imutáveis** dentro do componente.
- `@key` obrigatório em toda lista renderizada por `@foreach`.
- Componente que assina evento implementa `IAsyncDisposable` e **cancela a assinatura**.

## Streaming de progresso dos agentes

A UI consome `IAsyncEnumerable<RunEvent>` do `IRunEventStream`. Nunca faça polling.

```csharp
protected override async Task OnInitializedAsync()
{
    _cts = new CancellationTokenSource();
    _ = Task.Run(async () =>
    {
        await foreach (var evt in EventStream.SubscribeAsync(RunId, _cts.Token))
        {
            _events.Add(evt);
            await InvokeAsync(StateHasChanged);   // sempre via InvokeAsync
        }
    }, _cts.Token);
}

public async ValueTask DisposeAsync()
{
    await _cts.CancelAsync();
    _cts.Dispose();
}
```

- `StateHasChanged` a partir de thread de background **sempre** dentro de `InvokeAsync`.
- Faça *throttle* de atualização (ex.: no máximo 10 renders/s) para não afogar o circuito
  SignalR durante uma execução com muitos agentes.

## Segurança

- Todo conteúdo vindo de agente, issue ou modelo é **texto não confiável**:
  renderize como texto. `MarkupString` só sobre Markdown já sanitizado.
- Antiforgery habilitado; headers `CSP`, `HSTS`, `X-Content-Type-Options` configurados.
- Nenhum segredo trafega para o cliente. Nenhuma chamada a `gh`/`az` a partir de
  parâmetro de query sem validação e autorização.
- Aprovações humanas (plano, issues, merge) exigem uma ação explícita do usuário e
  ficam registradas com quem aprovou e quando.

## Acessibilidade e apresentação

- Contraste AA, foco visível, navegação por teclado, `aria-live="polite"` no log de
  execução (é a região que muda sozinha).
- A tela tem que ser legível **de longe**: esta UI é projetada em um telão de palestra.
