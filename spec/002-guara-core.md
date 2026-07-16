# Spec 002: `Guara.Core` — Modelos Internos, Estados e Pipeline

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Core`
**Depende de:** [Spec 001 (`Guara.Abstractions`)](001-guara-abstractions.md)
**Docs de referência:** [ARCHITECTURE](../docs/ARCHITECTURE.md) · [execution-flows](../docs/execution-flows.md) · [patterns](../docs/patterns.md) · [performance](../docs/performance.md) · [ADR-0002](../docs/adr/0002-comunicacao-por-eventos.md) · [ADR-0004](../docs/adr/0004-channel-para-filas-internas.md) · [ADR-0007](../docs/adr/0007-pipeline-de-middlewares.md)

## Problem

Contratos ([Spec 001](001-guara-abstractions.md)) não executam nada. Alguém precisa **materializar** o job em execução (`IJobContext` concreto), **compor o pipeline** de middlewares na ordem canônica, **governar as transições de estado** e **entregar eventos em processo** — tudo isso **sem conhecer** banco, ASP.NET ou Dashboard. Esse é o núcleo do framework: `Guara.Core`.

Quem depende: todos os motores (`Scheduler`, `Dispatcher`, `Worker`, `Executor`), o `Hosting` e os middlewares dos demais componentes. Sucesso = um núcleo determinístico, testável isoladamente (sem infra), de baixa alocação e AOT-safe.

## Scope

### In

- **`JobContext`** — implementação concreta de `IJobContext`, **pooled** (Object Pool).
- **Máquina de estados** — transições válidas de `JobState` e o guarda que as aplica (`JobStateMachine`).
- **Pipeline** — `JobPipelineBuilder` que compõe os `IJobMiddleware` registrados na **ordem canônica** e produz um `JobDelegate`.
- **Event bus em processo** — implementação default de `IEventPublisher` sobre `Channel<T>`, com fan-out para `IEventHandler<TEvent>`.
- **Middlewares genéricos e agnósticos** que não pertencem a outro componente: `ValidationMiddleware` (hook), `RetryMiddleware` (política de retry).
- **Abstrações comuns** de núcleo: uso de `TimeProvider` (relógio testável), guardas/validações internas, tipo de resultado interno se necessário.

### Out

- **Nenhuma** dependência de banco, ASP.NET, Dashboard, serialização concreta.
- **Nenhuma** extensão `AddGuara...()` — o wiring é do `Guara.Hosting` ([Spec 009](README.md)).
- Middlewares **de outros componentes**: `MetricsMiddleware`/`LoggingMiddleware` → `Guara.Diagnostics`; `AuthorizationMiddleware` → `Guara.Authorization`; `SerializationMiddleware` → `Guara.Serialization`; `ExecutionMiddleware` → `Guara.Executor`.
- Cálculo de agendamento (`Guara.Scheduler`), busca (`Guara.Dispatcher`), execução do método do job (`Guara.Executor`).
- Entrega **durável/distribuída** de eventos → `Guara.Cluster`/`Guara.Distributed`.

## Domain Model

### `JobContext` (impl de `IJobContext`)

Carrega o job ao longo do pipeline. Pooled e resetável. Campos: `Id`, `Descriptor`, `State`, `Attempt`, `Items` (property bag lazy), `CreatedAt`, `User?` (DD-5 da Spec 001). Nunca sobrevive à execução (retorna ao pool ao final).

### Máquina de estados (`JobStateMachine`)

Transições **válidas** (qualquer outra é rejeitada):

| De | Para |
|---|---|
| `Created` | `Enqueued`, `Scheduled` |
| `Scheduled` | `Enqueued` (quando vencido) |
| `Enqueued` | `Processing` |
| `Processing` | `Succeeded`, `Failed`, `Retrying` |
| `Retrying` | `Enqueued`, `Scheduled` |
| `Failed` | `Enqueued` (requeue manual) |
| `Succeeded` | — (terminal) |

### Pipeline (ordem canônica)

Definida aqui e imutável ([execution-flows](../docs/execution-flows.md), [ADR-0007](../docs/adr/0007-pipeline-de-middlewares.md)):

```
Validation → Authorization → Serialization → Middleware(custom)
           → Metrics → Logging → Retry → Executor → Success → Notifications
```

`JobPipelineBuilder` recebe os `IJobMiddleware` registrados, ordena pelos **slots canônicos** e produz um `JobDelegate` encadeado. Cada componente contribui com o middleware do seu slot; slots ausentes são no-op.

### Event bus em processo

`InProcessEventPublisher : IEventPublisher` publica em um `Channel<IGuaraEvent>`; consumidores registrados (`IEventHandler<TEvent>`) recebem via fan-out. Entrega **best-effort, em processo** (garantias fortes → Cluster/Distributed, DD-2).

## API Contract

Superfície .NET pública (formas ilustrativas):

```csharp
namespace Guara.Core;

public sealed class JobContext : IJobContext { /* pooled; Reset() interno */ }

public sealed class JobStateMachine
{
    public bool CanTransition(JobState from, JobState to);
    public JobState Transition(JobState from, JobState to); // lança se inválida
}

public sealed class JobPipelineBuilder
{
    public JobPipelineBuilder Use(IJobMiddleware middleware);
    public JobDelegate Build(); // encadeia na ordem canônica
}

public sealed class InProcessEventPublisher : IEventPublisher
{
    public ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken ct) where TEvent : IGuaraEvent;
}

// Middlewares genéricos de núcleo
public sealed class RetryMiddleware(RetryOptions options) : IJobMiddleware { /* ... */ }
public sealed class ValidationMiddleware : IJobMiddleware { /* hook */ }
```

`Guara.Core` referencia **apenas** `Guara.Abstractions` + BCL. Implementações são `sealed`; `internal` quando não precisam ser públicas para o `Hosting`.

## Authorization

Sem autorização própria. O `AuthorizationMiddleware` (slot Authorization) é de `Guara.Authorization` (Spec 021); `Core` apenas garante o **slot** e a passagem de `IJobContext.User`.

## Edge Cases & Failure Modes

- **Transição inválida** de estado → rejeição explícita (exceção de domínio), nunca transição silenciosa.
- **Middleware que curto-circuita** (não chama `next`) → pipeline encerra ali; estado final coerente.
- **Exceção em middleware** → capturada pelo `RetryMiddleware`/`Executor` conforme política; `JobContext` sempre retorna ao pool (via `finally`).
- **`JobContext` reusado** → `Reset()` limpa `Items`, `Attempt`, `State` antes de devolver ao pool (sem vazamento entre jobs).
- **Cancelamento** → `CancellationToken` propagado por todo o `JobDelegate`; efeito colateral externo já concluído não é revertido (regra transversal de [execution-flows](../docs/execution-flows.md)).
- **Handler de evento que lança** → não derruba o publisher; erro isolado por handler.

## Non-Functional Requirements

- `ValueTask` em todo o caminho do pipeline; `Channel<T>` no event bus ([ADR-0004](../docs/adr/0004-channel-para-filas-internas.md)).
- **Object Pool** para `JobContext` — alocação amortizada ~zero por job no hot path ([performance](../docs/performance.md)).
- **Thread-safe** por padrão; sem estado estático mutável.
- **AOT/Trimming-safe**; zero reflection (composição do pipeline por registro explícito, não varredura).
- Determinístico e testável sem infra (relógio via `TimeProvider`).

## Integrations

Nenhuma externa. Produz/consome os **eventos** definidos na Spec 001 dentro do processo.

## Acceptance Criteria

- **AC-1 — Referências.** *Dado* o build de `Guara.Core`, *então* referencia só `Guara.Abstractions` + BCL (sem storage/ASP.NET/Dashboard).
- **AC-2 — Ordem do pipeline.** *Dado* middlewares registrados fora de ordem, *quando* `Build()` roda, *então* a execução segue a ordem canônica documentada.
- **AC-3 — Curto-circuito.** *Dado* um middleware que não chama `next`, *quando* o job executa, *então* os middlewares posteriores não rodam e o resultado é coerente.
- **AC-4 — Transições válidas.** *Dado* `Processing`, *quando* transiciona para `Succeeded`/`Failed`/`Retrying`, *então* é aceito; *quando* transiciona para `Created`, *então* é rejeitado.
- **AC-5 — Pooling.** *Dado* N jobs em sequência, *quando* executados, *então* o número de `JobContext` alocados é limitado pelo pool (não cresce linearmente com N).
- **AC-6 — Reset sem vazamento.** *Dado* um `JobContext` reutilizado, *então* `Items`/`Attempt`/`State` do job anterior não são visíveis no próximo.
- **AC-7 — Event fan-out.** *Dado* um evento publicado com M handlers registrados, *então* todos os M recebem; um handler que lança não impede os demais.
- **AC-8 — Cancelamento.** *Dado* um `CancellationToken` cancelado no meio do pipeline, *então* a execução para de forma cooperativa e o estado fica consistente.
- **AC-9 — AOT.** *Dado* `PublishAot=true` num consumidor mínimo de `Core`, *então* sem warnings de trim/AOT originados aqui.

## Deferred Decisions

- **DD-1 — Política de retry default.** *Fallback:* `RetryMiddleware` com **3 tentativas** e back-off exponencial, sobrescrevível por job; jobs com efeito colateral irreversível declaram `0`. *Revisão:* Spec 008 (`Guara.Executor`).
- **DD-2 — Garantias de entrega de eventos.** *Fallback:* em processo, best-effort, fan-out assíncrono; entrega durável/at-least-once é de `Guara.Cluster`/`Guara.Distributed`. *Revisão:* Specs 025/026.
- **DD-3 — Relógio.** *Fallback:* usar `System.TimeProvider` (BCL) injetado, para testabilidade determinística. *Revisão:* nenhuma pendente.
- **DD-4 — `ValidationMiddleware`.** *Fallback:* `Core` fornece só o **hook/slot**; validação concreta (ex.: FluentValidation) é wiring de `Hosting`/`Error-handling`. *Revisão:* Spec 009.
- **DD-5 — Tipo de `Items`.** *Fallback:* `Dictionary<string, object?>` lazy; avaliar dicionário pooled se benchmark indicar pressão de GC. *Revisão:* durante benchmarks.

## Open Questions

_(vazio)_
