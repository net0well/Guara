# Spec 012: `Guara.Storage.SqlServer` — Storage SQL Server

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Storage.SqlServer`
**Implementa:** [Spec 004 (`Guara.Storage`)](004-guara-storage.md)
**Docs de referência:** [ADR-0003](../docs/adr/0003-abstracao-de-storage-por-provider.md) · [performance](../docs/performance.md)

## Problem

Muitos adotantes têm SQL Server. Precisamos de um provider durável, com **dequeue atômico sob concorrência** e lock distribuído, que passe o conformance kit e siga boas convenções de esquema (índices corretos, sem `nvarchar(max)` acidental).

## Scope

### In

- Implementação de `IStorage` sobre SQL Server; `UseSqlServerStorage(connectionString)`.
- Esquema + **migrations** idempotentes; índices de hot path.
- Dequeue atômico via `UPDATE ... WITH (READPAST, UPDLOCK, ROWLOCK) OUTPUT`.
- Lock distribuído via `sp_getapplock`; push opcional via polling (sem broker).

### Out

- Lógica de negócio; apenas persistência.

## Domain Model

- Tabelas (PascalCase, schema `guara`): `Jobs`, `Queues`, `Locks`, `Servers` (heartbeat), `Recurring` (recorrentes), `Continuations` (vínculos pai→filho) e `StateHistory` (timeline de estados — opcional, `EnableStateHistory`). Visão consolidada do esquema: [Spec 004](004-guara-storage.md).
- Índices: `(Queue, State, ScheduledFor, LeaseUntil)` para elegibilidade; PK em `Id`.
- `Capabilities`: transações `true`, lock distribuído `true` (app locks), server-side filter `true`.
- Acesso: leituras/escritas de hot path via SQL otimizado (Dapper/`SqlCommand`); esquema/migrations via EF Core (DD-1).

## API Contract

```csharp
namespace Microsoft.Extensions.DependencyInjection;
public static class SqlServerStorageExtensions
{
    public static IGuaraBuilder UseSqlServerStorage(this IGuaraBuilder builder, string connectionString,
        Action<SqlServerStorageOptions>? configure = null);
}
```

## Authorization

Acesso controlado por credenciais da connection string (via `Guara.Configuration`/segredos). Comandos parametrizados — **sem** concatenação de SQL (anti-injeção).

## Edge Cases & Failure Modes

- **Concorrência multi-nó** → `READPAST`+`UPDLOCK` garante que só um obtém a linha; os demais pulam.
- **Deadlock** → retry curto com back-off no dequeue.
- **Lease expira** → filtro por `LeaseUntil < now` reelege.
- **Migração em cluster** → migrations idempotentes e coordenadas por `sp_getapplock`.
- **Strings** → todo campo com `HasMaxLength`; nenhum `nvarchar(max)` implícito.

## Non-Functional Requirements

- Dequeue atômico e indexado; **sem N+1**, leituras de dashboard paginadas (`dotnet-skills:database-performance`).
- Comandos parametrizados; conexões via pooling.
- AOT: caminho quente sem reflection (EF só para migrations, fora do runtime crítico) — DD-1.

## Integrations

SQL Server (via `Microsoft.Data.SqlClient`); `ISerializer` para payloads.

## Acceptance Criteria

- **AC-1 — Conformance kit.** Passa 100% (Spec 004).
- **AC-2 — Dequeue atômico.** *Dado* K nós competindo, *então* cada job é processado por exatamente um.
- **AC-3 — Lease/visibility.** *Dado* lease expirado, *então* o job reaparece elegível.
- **AC-4 — Migrations idempotentes.** *Dado* aplicar migrations 2x/em paralelo, *então* o esquema final é consistente e sem erro.
- **AC-5 — Índices.** *Dado* o esquema, *então* existe índice cobrindo a query de elegibilidade.
- **AC-6 — Sem nvarchar(max).** *Dado* o esquema, *então* nenhuma coluna string ilimitada não intencional.
- **AC-7 — App lock.** *Dado* `TryAcquireAsync`, *então* o lock é exclusivo entre nós com TTL.

## Deferred Decisions

- **DD-1 — EF Core vs Dapper.** *Fallback:* migrations/esquema com EF Core (skill `dotnet-claude-kit:ef-core`); hot path (dequeue/update) com SQL otimizado. *Revisão:* durante benchmarks.
- **DD-2 — Versão mínima do SQL Server.** *Fallback:* SQL Server 2019+ / Azure SQL. *Revisão:* nenhuma.
- **DD-3 — Schema/prefixo de tabelas.** *Fallback:* schema `guara`, tabelas PascalCase; configurável via `SqlServerStorageOptions.Schema`. *Revisão:* feedback.
- **DD-4 — AutoMigrate (resolvido 2026-07-16).** *Decisão:* `SqlServerStorageOptions.AutoMigrate` (default **`true`**, estilo `PrepareSchemaIfNecessary` do Hangfire) aplica as migrations idempotentes no startup, coordenadas por `sp_getapplock`; em produção recomenda-se `false` + CLI `guara migrate` (spec 027) no pipeline.

## Open Questions

_(vazio)_
