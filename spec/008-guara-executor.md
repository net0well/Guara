# Spec 008: `Guara.Executor` — Execução do Job Pronto

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Executor`
**Depende de:** [Spec 001](001-guara-abstractions.md), [Spec 002](002-guara-core.md), [Spec 004](004-guara-storage.md)
**Docs de referência:** [execution-flows](../docs/execution-flows.md) · [patterns](../docs/patterns.md) · [ADR-0007](../docs/adr/0007-pipeline-de-middlewares.md)

## Problem

Recebe um job pronto, **executa o pipeline** (montado pelo Core), **invoca o método do job sem reflection**, atualiza o estado no storage e finaliza emitindo `JobCompleted`/`JobFailed`. É o coração da execução — e o ponto onde retry e idempotência importam mais.

## Scope

### In

- Corpo de `IExecutor`: obtém `JobContext` (pooled, Core), roda o `JobDelegate` (pipeline canônico), aplica política de retry, transiciona estado, emite eventos finais.
- **`ExecutionMiddleware`** (slot `Executor` do pipeline): invoca o método do job via **dispatch gerado** (`Guara.SourceGenerators`, Spec 029) — zero reflection.
- Transição final de estado atômica via `IJobStorage.UpdateStateAsync`.

### Out

- Composição/ordem do pipeline (é do Core, Spec 002), busca (Dispatcher), capacidade (Worker).
- Middlewares de outros slots (Metrics/Logging/Auth/Serialization).

## Domain Model

- **`IExecutor.ExecuteAsync(JobRecord, CancellationToken)`** — orquestra a execução de um job.
- Estados: `Enqueued/Scheduled → Processing → Succeeded | Failed | Retrying` (máquina do Core).
- **Retry**: `RetryMiddleware` (Core) decide reentrada; Executor persiste `Attempt` e `Retrying`/`Failed`.
- **Idempotência**: jobs com efeito colateral irreversível declaram `MaxAttempts=0` (regra dos docs de exemplo/jobs).

## API Contract

```csharp
namespace Guara.Executor;

public interface IExecutor
{
    ValueTask ExecuteAsync(JobRecord record, CancellationToken ct);
}

// slot de execução no pipeline
public sealed class ExecutionMiddleware(IJobInvoker invoker) : IJobMiddleware
{
    public ValueTask InvokeAsync(IJobContext ctx, JobDelegate next, CancellationToken ct);
}

// dispatch gerado (sem reflection) — impl em Guara.SourceGenerators
public interface IJobInvoker { ValueTask InvokeAsync(IJobContext ctx, CancellationToken ct); }
```

## Authorization

Não decide autorização; respeita o resultado do `AuthorizationMiddleware` (slot anterior). Job não autorizado nunca chega à invocação.

## Edge Cases & Failure Modes

- **Exceção no job** → capturada; `RetryMiddleware` decide `Retrying` (com back-off) ou `Failed` (esgotou tentativas).
- **Cancelamento** (shutdown/drain) → execução para de forma cooperativa; estado fica consistente; lease expira → re-processo.
- **Efeito colateral já concluído + cancelamento** → persistência do estado final usa token não-cancelável (regra transversal, [execution-flows](../docs/execution-flows.md)).
- **Job não encontrado no registry de tipos** (Spec 003) → `Failed` com motivo, sem instanciar tipo arbitrário.
- **Timeout por job** (opcional) → cancela via `CancellationTokenSource` linkado.
- **`JobContext` sempre devolvido ao pool** via `finally`.

## Non-Functional Requirements

- **Zero reflection** na invocação (dispatch gerado, [ADR-0005](../docs/adr/0005-source-generators-para-registro.md)); AOT-safe.
- `ValueTask` no caminho; alocação amortizada ~zero (pool do `JobContext`).
- Atualização de estado **atômica e idempotente** (Spec 004).
- Thread-safe; um Executor processa muitos jobs concorrentemente (chamado pelo Worker).

## Integrations

Roda o pipeline do Core (Spec 002); invoca via dispatch gerado (Spec 029); persiste via `IJobStorage` (Spec 004); emite `JobCompleted`/`JobFailed`.

## Acceptance Criteria

- **AC-1 — Executa o pronto.** *Dado* um `JobRecord`, *quando* `ExecuteAsync`, *então* o pipeline roda na ordem canônica e o método do job é invocado.
- **AC-2 — Sem reflection.** *Dado* `PublishAot=true`, *então* a invocação funciona sem warnings de trim/AOT.
- **AC-3 — Sucesso.** *Dado* um job que completa, *então* estado vira `Succeeded`, resultado persistido, `JobCompleted` emitido.
- **AC-4 — Retry.** *Dado* um job que falha com `MaxAttempts=3`, *então* re-tenta com back-off até 3 vezes; na 4ª falha vira `Failed`.
- **AC-5 — Sem retry (efeito irreversível).** *Dado* `MaxAttempts=0`, *quando* falha, *então* vira `Failed` imediatamente (sem re-tentar).
- **AC-6 — Cancelamento consistente.** *Dado* cancelamento no meio, *então* o estado não fica "meio-processado" e o lease expira para re-processo.
- **AC-7 — Persistência pós-efeito.** *Dado* um job que já disparou efeito externo e o request cancela, *então* o estado final é persistido mesmo assim (token não-cancelável).
- **AC-8 — Pool.** *Dado* muitos jobs, *então* `JobContext` é sempre devolvido ao pool (sem vazamento).

## Deferred Decisions

- **DD-1 — Política de retry default** (herda DD-1 da Spec 002). *Fallback:* 3 tentativas, back-off exponencial (2^n com jitter), `0` para efeito irreversível. *Revisão:* resolvida aqui — **confirmada**.
- **DD-2 — Continuations (resolvido).** *Decisão:* **no escopo 1.0** — especificado em [Spec 030](030-continuations.md); o Executor emite `JobCompleted`/`JobFailed` que disparam a promoção das continuações.
- **DD-3 — Timeout por job.** *Fallback:* opcional por job (atributo/opção), sem default global. *Revisão:* pós-MVP.
- **DD-4 — Armazenamento de resultado.** *Fallback:* resultado serializado (Spec 003) e persistido no `JobRecord`; truncado acima de um limite configurável. *Revisão:* Spec 004/010.

## Open Questions

_(vazio)_
