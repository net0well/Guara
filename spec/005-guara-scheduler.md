# Spec 005: `Guara.Scheduler` — Agendamento (Cron/Delay/Recurring)

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Scheduler`
**Depende de:** [Spec 001](001-guara-abstractions.md), [Spec 002](002-guara-core.md), [Spec 004](004-guara-storage.md)
**Docs de referência:** [execution-flows](../docs/execution-flows.md) · [ADR-0002](../docs/adr/0002-comunicacao-por-eventos.md) · [ADR-0009](../docs/adr/0009-politica-de-dependencias.md)

## Problem

Decidir **quando** um job roda — imediato (fire-and-forget), com atraso (delayed), por expressão cron ou recorrente — e recalcular o próximo disparo. O Scheduler **não executa, não busca, não persiste diretamente**: calcula e emite eventos. É também a casa da **API pública de agendamento** (`IGuaraClient`), a superfície mais usada pelos consumidores do framework.

## Scope

### In

- Corpo de `IScheduler`: cálculo de `NextRun` a partir de `ScheduleDescriptor` (imediato/delay/cron/recurring).
- **`IGuaraClient`** — API pública **em português** ([ADR-0010](../docs/adr/0010-api-do-usuario-em-portugues.md)): `Enfileirar` (fire-and-forget), `Agendar` (delayed), `AdicionarOuAtualizarRecorrente`, `Excluir`. *(Adição extend-only ao catálogo da Spec 001.)*
- Parser de cron **próprio** atrás de `ICronParser` (sem dependência de terceiros — [ADR-0009](../docs/adr/0009-politica-de-dependencias.md)), com suporte a timezone/DST.
- Registro de **recurring jobs** e recomputo de `NextRun` ao `JobCompleted`.

### Out

- Execução (`Executor`), busca (`Dispatcher`), persistência concreta (providers).
- Coordenação de qual nó agenda em cluster (usa `ILockProvider`; regra em `Guara.Cluster`).

## Domain Model

- **`ScheduleDescriptor`** (da Spec 001): `Immediate` | `Delay(TimeSpan)` | `Cron(expr, tz)` | `Recurring(id, cron, tz)`.
- **`IScheduler.GetNextOccurrence(ScheduleDescriptor, DateTimeOffset after)` → `DateTimeOffset?`**.
- **`RecurringJob`** — `id`, `cron`, `timezone`, `lastRun`, `nextRun`, `descriptor`.
- Fluxo: `IGuaraClient` cria `JobDescriptor`+`ScheduleDescriptor` → emite `JobCreated` → Scheduler calcula → emite `JobScheduled` ([execution-flows](../docs/execution-flows.md)).

## API Contract

```csharp
namespace Guara.Scheduler;

public interface IScheduler
{
    DateTimeOffset? GetNextOccurrence(ScheduleDescriptor schedule, DateTimeOffset after);
}

public interface IGuaraClient // métodos em português — ADR-0010
{
    ValueTask<JobId> EnfileirarAsync(JobDescriptor job, CancellationToken ct);
    ValueTask<JobId> AgendarAsync(JobDescriptor job, TimeSpan delay, CancellationToken ct);
    ValueTask AdicionarOuAtualizarRecorrenteAsync(string id, JobDescriptor job, string cron, TimeZoneInfo tz, CancellationToken ct);
    ValueTask ExcluirAsync(JobId id, CancellationToken ct);
}

public interface ICronParser { DateTimeOffset? GetNext(string expression, TimeZoneInfo tz, DateTimeOffset after); }
```

## Authorization

`IGuaraClient` pode exigir permissão de enfileiramento quando `Guara.Authorization` estiver ativo (Spec 021); por padrão, aberto no processo host.

## Edge Cases & Failure Modes

- **Cron inválido** → erro na configuração/registro, não em runtime silencioso.
- **Horário de verão / timezone** → cálculo via `TimeZoneInfo`; ocorrências ambíguas/inexistentes tratadas de forma determinística.
- **Recurring com execução perdida** (host offline no horário) → política de *misfire* (DD-2).
- **Relógio** → `TimeProvider` injetado (testável).
- **Recompute duplo** em cluster → só o líder recomputa recurring (usa `ILockProvider`).

## Non-Functional Requirements

- Cálculo puro e determinístico; `ValueTask` na API assíncrona.
- Zero reflection; AOT-safe; cron parser sem alocação por chamada quando possível.
- `IGuaraClient` é o caminho mais quente → baixa alocação.

## Integrations

Emite/consome eventos (Spec 001); usa `IJobStorage` (Spec 004) via contrato para persistir o agendamento; `ICronParser` é **implementação própria** (sem terceiros — [ADR-0009](../docs/adr/0009-politica-de-dependencias.md)).

## Acceptance Criteria

- **AC-1 — Não executa.** *Dado* o pacote, *então* não invoca métodos de job nem acessa provider concreto (só contratos).
- **AC-2 — Delay.** *Dado* `Schedule(job, 30s)` em T, *então* `NextRun == T+30s` e emite `JobScheduled`.
- **AC-3 — Cron.** *Dado* cron `0 3 * * *` e `after=02:00`, *então* `NextRun == 03:00` do mesmo dia (no tz configurado).
- **AC-4 — Recurring recomputa.** *Dado* um recurring concluído, *quando* `JobCompleted`, *então* o próximo `NextRun` é calculado e emitido.
- **AC-5 — Cron inválido.** *Dado* uma expressão inválida em `AdicionarOuAtualizarRecorrenteAsync`, *então* falha na chamada com mensagem clara.
- **AC-6 — DST.** *Dado* um horário inexistente por DST, *então* a próxima ocorrência é resolvida deterministicamente (documentada).
- **AC-7 — Líder único.** *Dado* cluster com N nós, *então* apenas um recomputa/agenda cada recurring.
- **AC-8 — Sem terceiros no cron.** *Dado* o build de `Guara.Scheduler`, *então* ele **não** referencia nenhuma biblioteca de cron de terceiros (parser é próprio, atrás de `ICronParser`).

## Deferred Decisions

- **DD-1 — Parser de cron (resolvido).** *Decisão:* **implementação própria** atrás de `ICronParser`, **sem Cronos** ([ADR-0009](../docs/adr/0009-politica-de-dependencias.md)) — mantém runtime livre de terceiros e AOT garantido; exige testes fortes de DST/timezone. Cronos permanece só como *fallback plugável* teórico, não referenciado.
- **DD-2 — Política de misfire.** *Fallback:* ao voltar online, executar **uma** ocorrência perdida e seguir para a próxima (não backfill de todas). *Revisão:* pós-MVP.
- **DD-3 — Timezone default.** *Fallback:* UTC quando não especificado. *Revisão:* nenhuma.

## Open Questions

_(vazio)_
