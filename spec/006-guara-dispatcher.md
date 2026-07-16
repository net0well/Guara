# Spec 006: `Guara.Dispatcher` — Busca de Jobs

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Dispatcher`
**Depende de:** [Spec 001](001-guara-abstractions.md), [Spec 004](004-guara-storage.md)
**Docs de referência:** [execution-flows](../docs/execution-flows.md) · [ADR-0002](../docs/adr/0002-comunicacao-por-eventos.md) · [ADR-0004](../docs/adr/0004-channel-para-filas-internas.md)

## Problem

Algo precisa **buscar** os jobs elegíveis no storage e sinalizar que há trabalho — respeitando lease, filas e backpressure — sem executar, agendar ou serializar. Esse é o `Dispatcher`. Isolá-lo permite trocar estratégias de busca (polling vs push) sem tocar em Worker/Executor.

## Scope

### In

- Corpo de `IDispatcher`: laço de busca que consome `IJobStorage.AcquireNextDueAsync` e emite `WorkerRequested`.
- Estratégias de busca: **polling** (intervalo) e **push** (notificação do provider, quando `Capabilities` suportar).
- Respeito a **filas nomeadas** e ordem/prioridade entre filas.
- **Backpressure** via `Channel<T>` (não busca mais do que o Worker consegue processar).

### Out

- Execução (`Executor`), alocação de capacidade (`Worker`), cálculo de horário (`Scheduler`).
- Como o job é adquirido atomicamente (é do contrato `IJobStorage`, Spec 004).

## Domain Model

- **`IDispatcher`** — inicia/para o laço de busca; publica `WorkerRequested(JobId)`.
- **`DispatcherOptions`** — `PollingInterval`, `FetchBatchSize`, `Queues` (ordem/prioridade), `MaxInFlight`.
- Fonte da verdade da elegibilidade é o storage (lease/visibility, Spec 004); o Dispatcher não decide horário.

## API Contract

```csharp
namespace Guara.Dispatcher;

public interface IDispatcher
{
    ValueTask StartAsync(CancellationToken ct);
    ValueTask StopAsync(CancellationToken ct);
}

public sealed class DispatcherOptions
{
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int FetchBatchSize { get; set; } = 10;
    public string[] Queues { get; set; } = ["default"];
    public int MaxInFlight { get; set; } = 100;
}
```

## Authorization

N/A (interno).

## Edge Cases & Failure Modes

- **Storage indisponível** → back-off exponencial no polling; não derruba o host; loga e continua tentando.
- **Nada elegível** → dorme até o próximo intervalo/notificação (sem busy-loop).
- **Backpressure** → se `MaxInFlight` atingido, para de buscar até liberar (canal `Wait`).
- **Push não suportado** → cai para polling automaticamente conforme `Capabilities`.
- **Cancelamento/shutdown** → para de buscar imediatamente; jobs já sinalizados seguem no Worker.

## Non-Functional Requirements

- `Channel<T>` para o fluxo de `WorkerRequested` ([ADR-0004](../docs/adr/0004-channel-para-filas-internas.md)); sem busy-wait.
- `ValueTask`, cancelamento cooperativo, baixa alocação por ciclo.
- Thread-safe; um Dispatcher pode ter múltiplos leitores.

## Integrations

Usa `IJobStorage` (Spec 004) e emite `WorkerRequested` consumido pelo `Worker` (Spec 007).

## Acceptance Criteria

- **AC-1 — Só busca.** *Dado* o pacote, *então* não executa job nem calcula horário.
- **AC-2 — Emite ao encontrar.** *Dado* um job elegível no storage, *quando* o Dispatcher busca, *então* emite `WorkerRequested` uma única vez para aquele job.
- **AC-3 — Backpressure.** *Dado* `MaxInFlight` atingido, *então* o Dispatcher pausa a busca até haver capacidade.
- **AC-4 — Sem busy-loop.** *Dado* fila vazia, *então* a CPU fica ociosa até o próximo intervalo/notificação.
- **AC-5 — Resiliência a storage.** *Dado* storage temporariamente indisponível, *então* aplica back-off e se recupera sem crash.
- **AC-6 — Prioridade de filas.** *Dado* filas `["alta","default"]`, *então* jobs de `alta` são buscados antes de `default`.
- **AC-7 — Fallback de estratégia.** *Dado* provider sem push, *então* usa polling automaticamente.

## Deferred Decisions

- **DD-1 — Push vs polling default.** *Fallback:* polling 5s; push habilitado quando `Capabilities.SupportsServerSideTimers`/notificação existir. *Revisão:* por provider.
- **DD-2 — Prioridade entre filas.** *Fallback:* ordem de configuração (lista ordenada); sem pesos. *Revisão:* pós-MVP se houver demanda.
- **DD-3 — FetchBatchSize default.** *Fallback:* 10. *Revisão:* durante benchmarks.

## Open Questions

_(vazio)_
