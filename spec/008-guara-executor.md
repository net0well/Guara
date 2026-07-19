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
- **Idempotência**: jobs com efeito colateral irreversível declaram `[GuaraRetentativas(0)]` ([Spec 036](036-atributos-de-job.md)).

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

- **DD-1 — Política de retry default** (herda DD-1 da Spec 002). *Fallback:* 3 tentativas, back-off exponencial (2^n com jitter), `0` para efeito irreversível. *Revisão:* resolvida aqui — **confirmada**. **Semântica-alvo (2026-07-17, [semantics](../docs/semantics.md)): retentativa persistente** — falha grava `Retrying` + reagendamento com back-off e `Attempt` no storage (sobrevive a restart; dashboard mostra a contagem real); a retentativa in-process atual é interim. Garantia de entrega do Guará: **at-least-once**.
- **DD-2 — Continuations (resolvido).** *Decisão:* **no escopo 1.0** — especificado em [Spec 030](030-continuations.md); o Executor emite `JobCompleted`/`JobFailed` que disparam a promoção das continuações.
- **DD-3 — Timeout por job.** *Fallback:* opcional por job (atributo/opção), sem default global. *Revisão:* pós-MVP.
- **DD-4 — Armazenamento de resultado.** *Fallback:* resultado serializado (Spec 003) e persistido no `JobRecord`; truncado acima de um limite configurável. *Revisão:* Spec 004/010.

> **Implementação (2026-07-17):** `GuaraExecutor` entregue. **`IExecutor` vive em `Guara.Abstractions`** com assinatura `ExecuteAsync(JobId, ct)` (não `JobRecord` — evitaria que o contrato dependesse de tipos do Storage; o executor busca o registro). `IJobInvoker` também em Abstractions; até o source generator (spec 029), o invoker é o `RegistryJobInvoker` sobre `JobHandlerRegistry` (registro manual — infraestrutura temporária). Sucesso/falha persistidos com **`CancellationToken.None`** (AC-7); cancelamento deixa o estado intocado — lease expira e o job reprocessa (AC-6).

> **Implementação (2026-07-19) — metadados declarados (spec 036) e invocação gerada (spec 029):** o registry ganhou handlers com `IServiceProvider` e metadados (`JobExecutionMetadata`), materializado por factory que aplica os `IJobModule` gerados; `RegistryJobInvoker` resolve serviços — o registro manual continua para bootstrap simples. O executor consulta `IJobMetadataProvider` e honra: **retentativas por job** (sobrepõe a política global), **tempo limite** (CTS linkado; token honrado = falha que segue a política de retentativa; token ignorado + completou = `Succeeded` + aviso — DD-3 concluída) e o **gate de exclusão mútua** (lock `guara:mutex:*` com TTL 10min via `ILockProvider`; ocupado → `RescheduleAsync` devolve à fila sem consumir tentativa; espera limitada opcional). O gate vive no executor (não como middleware) para não depender de ordenação com os middlewares de diagnóstico.

> **Implementação (2026-07-18) — retentativa persistente (DD-1 concluída, semântica final):** a decisão de retentativa saiu do pipeline e virou persistência: na falha com tentativas restantes (`Attempt < RetryOptions.MaxAttempts`), o executor chama `IJobStorage.ScheduleRetryAsync` (atômico: `Retrying` + motivo + `Attempt+1` + `ScheduledFor = agora + Backoff(attempt)` + posse liberada) e emite o evento novo **`JobRetryScheduled`**; a reexecução é adquirida pelo dispatcher como qualquer job vencido — sobrevive a restart e a contagem real fica no storage (AC-4: MaxAttempts=3 → 4 execuções no total; AC-5: `MaxAttempts=0` falha direto). `JobFailed` só dispara na falha definitiva (o que faz `OnAnyFinishedState` das continuações esperar as retentativas, spec 030). O contexto é semeado com o `Attempt` persistido (`JobContext.Initialize(id, descriptor, attempt)`). Cancelamento não consome tentativa. **`RetryMiddleware` deixou de compor o pipeline default** e permanece como middleware opcional em processo (oscilações rápidas dentro de uma mesma tentativa, sem tocar o storage); `RetryOptions` agora governa a política persistente. O per-job `[GuaraRetentativas]` continua chegando com os atributos (spec 036).

## Open Questions

_(vazio)_
