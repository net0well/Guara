# ADR-0007 — Pipeline de Middlewares para Execução de Jobs

- **Status:** Aceito
- **Data:** 2026-07-16

## Contexto

A execução de um Job tem muitas responsabilidades transversais: validação, autorização, serialização, métricas, logging, retry, notificações. Colocá-las no Executor cria uma classe-monstro; espalhá-las cria acoplamento. Precisamos de composição e de um ponto de extensão para o usuário.

## Decisão

Todo Job percorre um **pipeline de middlewares**, no modelo do ASP.NET Core (`context` + `next`). Ordem fixa:

```
Validation → Authorization → Serialization → Middleware(custom)
           → Metrics → Logging → Retry → Executor → Success → Notifications
```

```csharp
public interface IJobMiddleware
{
    ValueTask InvokeAsync(JobContext context, JobDelegate next, CancellationToken ct);
}
```

O slot `Middleware` é o ponto de extensão do usuário. Composição sobre herança: comportamento é montado, não herdado.

## Consequências

**Ganhos:** cada preocupação isolada e testável; usuário estende sem tocar no núcleo; ordem explícita e documentada; reaproveita mental model do ASP.NET Core.

**Custos:** overhead por camada (mitigado com `ValueTask` e Object Pool no `JobContext`); ordem incorreta de middlewares é um erro sutil — mitigado por composição centralizada e testes.

Detalhado em [../execution-flows.md](../execution-flows.md) e [../patterns.md](../patterns.md).
