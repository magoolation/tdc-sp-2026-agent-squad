---
applyTo: "tests/**/*.cs"
description: "Como escrever testes neste repositório: xUnit v3, MTP, AwesomeAssertions, determinismo."
---

# Testes

## Stack

- **xUnit v3** sobre **Microsoft.Testing.Platform** (não VSTest).
- Asserções: **AwesomeAssertions** (`value.Should().Be(...)`).
- Dublês: **NSubstitute**. Sem framework de mock pesado, sem `Moq`.
- Dados: `[Theory]` + `[InlineData]`/`[MemberData]`. Geração determinística.

## Estrutura

```csharp
public sealed class WavePlannerTests
{
    [Fact]
    public void Plan_Should_PutDependentItemsInLaterWaves_When_DependencyDeclared()
    {
        // Arrange
        var items = new[]
        {
            WorkItem.Create(1, "domain model"),
            WorkItem.Create(2, "api endpoint", dependsOn: [1]),
        };
        var planner = new WavePlanner(NullLogger<WavePlanner>.Instance, TimeProvider.System);

        // Act
        var waves = planner.Plan(items);

        // Assert
        waves.Should().HaveCount(2);
        waves[0].Should().ContainSingle(i => i.Number == 1);
        waves[1].Should().ContainSingle(i => i.Number == 2);
    }
}
```

## Regras

1. Nome: `Metodo_Should_ComportamentoEsperado_When_Condicao`.
2. Arrange / Act / Assert separados por linha em branco, com os comentários acima.
3. **Determinismo obrigatório:**
   - Tempo: injete `TimeProvider`; em teste use `FakeTimeProvider`. Nunca `DateTime.Now`.
   - Aleatoriedade: `new Random(seed)` fixo.
   - Sem `Thread.Sleep`; sem espera por relógio de parede.
   - Sem dependência de ordem entre testes; sem estado compartilhado estático.
   - Sem rede, sem disco, sem processo externo em teste **unitário**.
4. Teste de integração (toca disco/processo/rede) é marcado e isolado:

```csharp
[Trait("Category", "Integration")]
public sealed class GitWorktreeServiceIntegrationTests : IAsyncLifetime { /* ... */ }
```

5. Teste comportamento observável. Evite asserção sobre "o mock foi chamado" quando o
   efeito é verificável de outra forma.
6. Um conceito por teste. Vários `Should()` sobre o mesmo resultado tudo bem; vários
   cenários no mesmo `[Fact]`, não.
7. Cobrir o caminho de erro: cancelamento, timeout, saída não-zero de processo,
   JSON inválido vindo do modelo, conflito de merge.
8. Nenhum `Skip` sem link para issue aberta.
9. Para código que chama modelo: use um `IChatClient` falso que devolve resposta fixa.
   **Nunca** chame o modelo de verdade em teste automatizado.

## Nomeação de projeto e pasta

`tests/<ProjetoSobTeste>.Tests/` espelhando a estrutura de pastas do projeto testado.
