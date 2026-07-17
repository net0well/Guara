# Spec 004: `Guara.Storage` — Contratos de Storage

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Storage` (pacote de contratos — **nunca implementa**)
**Depende de:** [Spec 001](001-guara-abstractions.md), [Spec 003](003-guara-serialization.md)
**Realizado por:** Specs 011–015 (`Guara.Storage.Memory/SqlServer/PostgreSql/Redis/Mongo`)
**Docs de referência:** [components](../docs/components.md) · [dependency-rules](../docs/dependency-rules.md) · [ADR-0003](../docs/adr/0003-abstracao-de-storage-por-provider.md) · [performance](../docs/performance.md)

## Problem

O Guará precisa persistir jobs, filas, resultados e locks em backends muito diferentes (memória, SQL Server, PostgreSQL, Redis, Mongo) **sem** que os motores conheçam qualquer banco. Espelhando o EF Core, `Guara.Storage` **define os contratos** e nunca implementa; cada provider implementa apenas o contrato. Trocar de backend = trocar uma linha ([ADR-0003](../docs/adr/0003-abstracao-de-storage-por-provider.md)).

Como será open-source com muitos providers (inclusive de terceiros), este pacote precisa de um **contrato preciso**, um **modelo de capabilities** (backends diferem) e um **kit de conformidade** que todo provider deve passar.

## Scope

### In

- Contratos: `IStorage` (fachada), `IJobStorage`, `IQueueStorage`, `ILockProvider`, `ITransaction`.
- **`StorageCapabilities`** — flags do que o provider suporta (transações, locks distribuídos, timers server-side, filtros no servidor).
- **Semântica de aquisição atômica** de job (lease/visibility timeout) para evitar processamento duplicado entre nós.
- Modelos de registro persistido: `JobRecord`, `JobState` (da Spec 001), tentativa, timestamps, resultado/erro.
- Requisito de um **conformance test kit** (contrato de testes que todos os providers passam).

### Out

- Qualquer implementação concreta (é das Specs 011–015).
- Extensão `AddGuara...()`/`Use...Storage()` (cada provider expõe a sua).
- Coordenação de cluster de alto nível (é de `Guara.Cluster`, que usa `ILockProvider`).

## Domain Model

- **`JobRecord`** — job persistido: `JobId`, `JobDescriptor` (serializado), `JobState`, `Attempt`, `Queue`, `CreatedAt`, `ScheduledFor?`, `LeaseUntil?`, `Result?`/`Error?`.
- **`IJobStorage`** — persistência e transição de jobs; aquisição atômica do próximo job vencido (com lease).
- **`IQueueStorage`** — enfileirar/desenfileirar por fila nomeada.
- **`ILockProvider`** — lock distribuído com **TTL** (acquire/renew/release); base para dedupe e cluster.
- **`ITransaction`** — unidade de trabalho opcional; providers sem transação declaram `Capabilities` sem a flag e usam operações atômicas equivalentes.
- **`StorageCapabilities`** — `SupportsTransactions`, `SupportsDistributedLock`, `SupportsServerSideTimers`, `SupportsServerSideFilter`.

### Esquema persistido (visão consolidada — 2026-07-16)

Estruturas que os providers materializam (nomes na convenção de cada backend, sob schema/prefixo `guara` isolado das tabelas da aplicação):

| Estrutura | Conteúdo | Contrato |
|---|---|---|
| `Jobs` | `JobRecord`: descriptor serializado, estado atual, tentativa, fila, `ScheduledFor`, `LeaseUntil`, resultado/erro | `IJobStorage` |
| `Queues` | Metadados/introspecção de filas | `IQueueStorage` |
| `Locks` | Locks distribuídos com TTL | `ILockProvider` |
| `Servers` | Nós/heartbeat ([Spec 010](010-guara-server.md)) | contrato definido com a Spec 010 |
| `Recurring` | Recorrentes: id, agenda (cron/intervalo), timezone, vigência (início/fim), descrição, calendário, último/próximo disparo, descriptor | `IRecurringStorage` — **adição extend-only**, definida com a [Spec 005](005-guara-scheduler.md)/[038](038-agendamento-fluente.md) |
| `Calendars` | Calendários reutilizáveis (datas/dias/janelas excluídas — [Spec 038](038-agendamento-fluente.md)) | `IRecurringStorage` |
| `Continuations` | Vínculo pai→filho + gatilho ([Spec 030](030-continuations.md)) | `IContinuationStorage` — **adição extend-only**, definida com a Spec 030 |
| `StateHistory` | Linha do tempo de transições de estado (timeline do dashboard, [Spec 022](022-guara-dashboard-api.md)) — **opcional** | Habilitada por `EnableStateHistory` (default `true`), com retenção própria |

> Sem tabelas genéricas `Hash`/`Set`/`List`/`Counter` (modelo Hangfire): o Guará usa estruturas **tipadas e legíveis**, consultáveis por SQL comum. As opções de instalação são as mesmas do Hangfire: **mesmo banco da aplicação** (isolado pelo schema `guara`, sem tocar no DbContext do usuário), **banco separado** (outra connection string) ou **memória** (`UseMemoryStorage()`).

## API Contract

```csharp
namespace Guara.Storage;

public interface IStorage
{
    StorageCapabilities Capabilities { get; }
    IJobStorage Jobs { get; }
    IQueueStorage Queues { get; }
    ILockProvider Locks { get; }
    ValueTask<ITransaction> BeginTransactionAsync(CancellationToken ct);
}

public interface IJobStorage
{
    ValueTask<JobId> CreateAsync(JobRecord record, CancellationToken ct);
    ValueTask<JobRecord?> AcquireNextDueAsync(string queue, TimeSpan lease, DateTimeOffset now, CancellationToken ct);
    ValueTask<bool> RenewLeaseAsync(JobId id, TimeSpan lease, CancellationToken ct); // false = posse perdida
    ValueTask UpdateStateAsync(JobId id, JobState state, string? resultOrError, CancellationToken ct);
    ValueTask<JobRecord?> GetAsync(JobId id, CancellationToken ct);
    ValueTask<IReadOnlyList<JobRecord>> ListAsync(JobQuery query, CancellationToken ct); // paginada, teto MaxPageSize=100
}

public interface ILockProvider
{
    ValueTask<ILockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct);
}
```

- Tudo `ValueTask` + `CancellationToken` último. Contratos vivem aqui; **nenhuma** implementação.

> **Implementação (2026-07-16):**
> - `RenewLeaseAsync` retorna **`ValueTask<bool>`** (`false` = posse perdida) — o Worker precisa do sinal para abortar e evitar dupla execução (spec 007, AC-7).
> - `IQueueStorage` ficou como **introspecção** (`GetQueuesAsync`/`GetLengthAsync`): enfileirar = `CreateAsync` com estado `Enqueued`; desenfileirar = `AcquireNextDueAsync` — sem duplicar operações. Extensível depois (extend-only).
> - `ILockHandle` (`IAsyncDisposable` + `RenewAsync` → bool) com posse por token: só o dono renova/libera.
> - `JobQuery` com `MaxPageSize = 100` (AC-7).
> - **Conformance kit** implementado como classe abstrata `StorageConformanceTests` (18 testes: aquisição atômica concorrente, lease/visibility, renovação, idempotência, paginação, locks TTL) — hoje em `tests/Guara.Storage.Memory.Tests/Conformance/`; será extraído para pacote compartilhado quando o segundo provider (specs 012+) chegar.

## Authorization

N/A no contrato. Providers **não** confiam no payload para resolver tipos (ver [Spec 003](003-guara-serialization.md)); acesso ao storage é responsabilidade de infra (connection string/segredos via `Configuration`).

## Edge Cases & Failure Modes

- **Aquisição concorrente** (vários nós): `AcquireNextDueAsync` é **atômica** — no máximo um nó obtém o job; os demais recebem `null`.
- **Worker morre com lease ativo**: ao expirar o `LeaseUntil`, o job volta a ser elegível (visibility timeout).
- **Update de estado idempotente**: reaplicar a mesma transição não corrompe o registro.
- **Provider sem transação**: usa operações atômicas equivalentes; nunca "meia-gravação".
- **Relógio distribuído**: `now` é injetado (via `TimeProvider`); providers server-side podem usar o relógio do servidor — declarado em `Capabilities`.
- **Retenção**: jobs concluídos são purgados por política (DD-3).

## Non-Functional Requirements

- Contrato **provider-agnóstico**; nenhuma suposição de SQL/NoSQL específica.
- `ValueTask`, baixa alocação, cancelamento cooperativo.
- Leituras do Dashboard **paginadas e limitadas** (skill `dotnet-skills:database-performance` — sem N+1, com limites).
- **Conformance kit**: uma suíte compartilhada de testes que todo provider (inclusive de terceiros) executa para provar conformidade.

## Integrations

Nenhuma direta. É o ponto de extensão para bancos; usa `ISerializer` (Spec 003) para (de)serializar `JobDescriptor`/resultado.

## Acceptance Criteria

- **AC-1 — Só contratos.** *Dado* o build de `Guara.Storage`, *então* não há classe concreta de acesso a banco; só interfaces, records e enums.
- **AC-2 — Aquisição atômica.** *Dado* 2 nós chamando `AcquireNextDueAsync` para o mesmo job, *então* exatamente um recebe o `JobRecord` e o outro recebe `null`.
- **AC-3 — Lease/visibility.** *Dado* um job adquirido cujo lease expira sem renovação, *quando* `now > LeaseUntil`, *então* o job volta a ser elegível.
- **AC-4 — Idempotência de estado.** *Dado* a mesma `UpdateStateAsync` aplicada duas vezes, *então* o registro final é o mesmo (sem duplicar efeitos).
- **AC-5 — Capabilities honestas.** *Dado* um provider, *então* `Capabilities` reflete exatamente o que ele suporta; consumidores checam antes de usar recursos opcionais.
- **AC-6 — Conformance kit.** *Dado* qualquer provider, *quando* roda o conformance kit, *então* passa 100% (AC-2..AC-4 inclusos).
- **AC-7 — Paginação limitada.** *Dado* uma consulta de listagem, *então* há limite máximo de página (sem retorno ilimitado).

## Deferred Decisions

- **DD-1 — Representação de `JobId`** (herdada da Spec 001). *Fallback:* `string` opaco; providers numéricos mapeiam internamente. *Revisão:* resolvida aqui — **mantida `string`**.
- **DD-2 — Concorrência.** *Fallback:* otimista por padrão (versão/rowversion); pessimista via `ILockProvider` quando o backend suportar. *Revisão:* por provider (Specs 011–015).
- **DD-3 — Retenção/purga.** *Fallback:* jobs `Succeeded` retidos por 24h, `Failed` por 7 dias, purga por job de manutenção; configurável. *Revisão:* Spec 010 (`Guara.Server`).
- **DD-4 — Lease default.** *Fallback:* 5 minutos, renovável pelo Worker durante execução. *Revisão:* Spec 007 (`Guara.Worker`).
- **DD-5 — Prioridade/múltiplas filas.** *Fallback:* filas nomeadas com ordem FIFO por fila; prioridade entre filas por ordem de configuração. *Revisão:* Spec 006 (`Guara.Dispatcher`).

## Open Questions

_(vazio)_
