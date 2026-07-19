# Spec 019: `Guara.Extensions` — Extensões Utilitárias

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Extensions`
**Depende de:** [Spec 001](001-guara-abstractions.md)
**Docs de referência:** [naming-conventions](../docs/naming-conventions.md) · [performance](../docs/performance.md)

## Problem

Alguns utilitários transversais (helpers de tempo, guardas, extensões ergonômicas da API pública) são usados por vários componentes. Sem um lar comum, eles se espalham e duplicam. `Guara.Extensions` concentra **açúcar sintático e helpers** — sem regra de negócio e sem virar um "utilitário genérico" inchado.

## Scope

### In

- Extensões ergonômicas sobre `IGuaraClient` (ex.: `Enfileirar(Expression<Action>)` tipado, sobrecargas de conveniência) — nomes em português ([ADR-0010](../docs/adr/0010-api-do-usuario-em-portugues.md)).
- Guardas/validações internas reutilizáveis (`Guard.NotNull`, etc.) — **internal**, não API pública.
- Helpers de tempo/relógio compatíveis com `TimeProvider`.

### Out

- Regra de negócio, estado, I/O.
- Virar um "Common" genérico — cada helper precisa de justificativa de uso por ≥2 componentes.

## Domain Model

- Coleção de métodos de extensão puros e stateless.
- Nada que introduza dependência nova além de `Guara.Abstractions` + BCL.

## API Contract

```csharp
namespace Guara; // extensões da API pública

public static class GuaraClientExtensions
{
    public static ValueTask<JobId> EnfileirarAsync(this IGuaraClient client, Expression<Func<Task>> methodCall, CancellationToken ct = default);
    public static ValueTask<JobId> AgendarAsync(this IGuaraClient client, Expression<Func<Task>> methodCall, TimeSpan delay, CancellationToken ct = default);
}
```

> A tradução de `Expression` → `JobDescriptor` é **compilada por source generator** (Spec 029), não avaliada por reflection em runtime.

## Authorization

N/A.

## Edge Cases & Failure Modes

- **Expression não suportada** (não é uma chamada de método simples) → erro de compilação (analyzer) ou exceção clara.
- **Captura de closure com estado não serializável** → detectada pelo analyzer/source gen; mensagem clara.

## Non-Functional Requirements

- Puro/stateless; zero alocação além do necessário.
- Sem reflection em runtime (Expression resolvida em compilação).
- AOT/Trimming-safe.

## Integrations

Depende só de `Guara.Abstractions`; coopera com `Guara.SourceGenerators` (Spec 029) para a tradução de expressões.

## Acceptance Criteria

- **AC-1 — Enfileirar tipado.** *Dado* `client.EnfileirarAsync(() => svc.FazerAlgo())`, *então* um `JobDescriptor` correto é criado e enfileirado.
- **AC-2 — Sem reflection.** *Dado* `PublishAot=true`, *então* a tradução de expressão funciona (via source gen), sem warnings.
- **AC-3 — Expressão inválida.** *Dado* uma expressão não suportada, *então* há erro claro (compilação ou runtime).
- **AC-4 — Escopo enxuto.** *Dado* o pacote, *então* cada helper é usado por ≥2 componentes (sem "utilitário órfão").
- **AC-5 — Sem dependências extras.** *Dado* o build, *então* referencia só `Guara.Abstractions` + BCL.

## Deferred Decisions

- **DD-1 — Superfície de conveniência.** *Fallback:* começar mínimo (Enqueue/Schedule tipados); crescer sob demanda real. *Revisão:* feedback da comunidade.

> **Implementação (2026-07-19) — decisão de arquitetura:** a API de `Expression` (`EnfileirarAsync(() => svc.Fazer(x))`) foi **rejeitada** após análise: traduzi-la "em compilação" é inviável — interceptors não alcançam variáveis capturadas em closure, e avaliar a árvore em runtime exige reflection sobre os campos da closure (exatamente o que o Hangfire faz e o ADR-0005 proíbe; quebra AOT). A forma tipada canônica do Guará é a **factory de descritor gerada por job** (spec 029): `jobs.EnfileirarAsync(RelatorioJobsGuara.GerarAsync(42))` — argumentos serializados em compilação, fila/metadados dos atributos aplicados na criação, e **assinatura errada é erro de compilação** (mais seguro que a lambda do Hangfire, que quebra em runtime). Com isso o pacote `Guara.Extensions` ficou **sem conteúdo próprio no 1.0** (AC-4: nenhum helper órfão — GuaraDatas vive no Scheduler junto do builder que o usa); ele passa a existir quando houver açúcar realmente transversal. AC-1/AC-2 são satisfeitos pela factory gerada; AC-3 virou diagnóstico de build do generator.

## Open Questions

_(vazio)_
