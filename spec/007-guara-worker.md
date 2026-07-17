# Spec 007: `Guara.Worker` — Execução de Jobs (Capacidade)

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Worker`
**Depende de:** [Spec 001](001-guara-abstractions.md), [Spec 004](004-guara-storage.md)
**Docs de referência:** [execution-flows](../docs/execution-flows.md) · [performance](../docs/performance.md) · [ADR-0004](../docs/adr/0004-channel-para-filas-internas.md)

## Problem

O Worker gerencia **capacidade**: quantos jobs rodam ao mesmo tempo, consome os `WorkerRequested`, renova o lease durante a execução e entrega o job pronto ao `Executor`. Ele **não sabe** como o job é executado por dentro (isso é do `Executor`) nem como foi buscado.

## Scope

### In

- Corpo de `IWorker`: pool de slots concorrentes (grau de paralelismo), consumo de `WorkerRequested`, emissão de `ExecutorStarted`.
- **Renovação de lease** enquanto o job executa (evita re-despacho por expiração).
- **Shutdown gracioso** (drain): termina os em andamento, para de aceitar novos.
- Limites de concorrência global e por fila.

### Out

- Execução do método do job / pipeline (é do `Executor`, Spec 008).
- Busca (é do `Dispatcher`, Spec 006).

## Domain Model

- **`IWorker`** — start/stop; mantém `N` slots; para cada `WorkerRequested`, adquire um slot e chama o `Executor`.
- **`WorkerOptions`** — `MaxConcurrency`, `PerQueueConcurrency`, `ShutdownDrainTimeout`, `LeaseRenewInterval`.
- Backpressure: o número de slots limita quantos `WorkerRequested` são consumidos.

## API Contract

```csharp
namespace Guara.Worker;

public interface IWorker
{
    ValueTask StartAsync(CancellationToken ct);
    ValueTask StopAsync(CancellationToken ct); // drain gracioso
}

public sealed class WorkerOptions
{
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;
    public IReadOnlyDictionary<string,int>? PerQueueConcurrency { get; set; }
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan LeaseRenewInterval { get; set; } = TimeSpan.FromMinutes(2);
}
```

## Authorization

N/A (interno).

## Edge Cases & Failure Modes

- **Shutdown** → para de consumir `WorkerRequested`; aguarda os em andamento até `ShutdownDrainTimeout`; o que passar do timeout é cancelado e o lease expira (job volta à fila).
- **Job trava (hang)** → lease não renovado além do previsto? Renovação periódica mantém posse; timeout de execução é do Executor/política.
- **Excesso de sinais** → slots limitam; sinais extras esperam (canal `Wait`).
- **Falha ao renovar lease** (storage caiu) → aborta o job com segurança para evitar dupla execução quando o lease expirar em outro nó.
- **Crash do processo** → lease expira; outro nó re-processa (visibility timeout, Spec 004).

## Non-Functional Requirements

- Concorrência via `Channel<T>` + `SemaphoreSlim`/slots, **sem locks grosseiros** ([ADR-0004](../docs/adr/0004-channel-para-filas-internas.md), skill `csharp-concurrency-patterns`).
- `ValueTask`, cancelamento cooperativo ponta a ponta.
- Drain determinístico; nenhum job perdido no shutdown normal.
- Thread-safe por padrão.

## Integrations

Consome `WorkerRequested` (Dispatcher), emite `ExecutorStarted` (Executor), renova lease via `IJobStorage` (Spec 004).

## Acceptance Criteria

- **AC-1 — Só capacidade.** *Dado* o pacote, *então* não invoca o método do job nem monta pipeline (delega ao Executor).
- **AC-2 — Respeita concorrência.** *Dado* `MaxConcurrency=4` e 100 sinais, *então* no máximo 4 jobs rodam simultaneamente.
- **AC-3 — Renova lease.** *Dado* um job longo, *então* o lease é renovado em `LeaseRenewInterval` e o job não é re-despachado.
- **AC-4 — Drain.** *Dado* `StopAsync` com jobs em andamento, *então* eles terminam (até o timeout) e nenhum novo é aceito.
- **AC-5 — Timeout de drain.** *Dado* um job que excede `ShutdownDrainTimeout`, *então* é cancelado e seu lease expira para re-processo.
- **AC-6 — Concorrência por fila.** *Dado* `PerQueueConcurrency`, *então* cada fila respeita seu próprio limite.
- **AC-7 — Sem dupla execução.** *Dado* falha ao renovar lease, *então* o Worker aborta o job localmente antes que outro nó o assuma.

## Deferred Decisions

- **DD-1 — `MaxConcurrency` default.** *Fallback:* `Environment.ProcessorCount`. *Revisão:* benchmarks.
- **DD-2 — Timeout de execução por job.** *Fallback:* sem timeout global; opcional por job (o Executor aplica). *Revisão:* Spec 008.
- **DD-3 — Drain timeout default.** *Fallback:* 30s. *Revisão:* Spec 010 (`Guara.Server`).

> **Implementação (2026-07-17):** `GuaraWorker` entregue: é `IEventHandler<WorkerRequested>` gravando num `Channel<JobId>` limitado (`MaxConcurrency*2`, `FullMode.Wait` → backpressure ao Dispatcher); N slots concorrentes; **renovação de lease** em loop paralelo por job — renovação negada cancela a execução local via CTS linkado (AC-7); publica `ExecutorStarted`; drain em 2 fases (para de aceitar → aguarda até `ShutdownDrainTimeout` → cancela excedentes; itens na fila interna não iniciados são descartados e recuperados por expiração de lease). `PerQueueConcurrency` fica extend-only para depois. Logging estruturado via `ILogger` (M.E.Logging.Abstractions — plataforma, ADR-0009).

## Open Questions

_(vazio)_
