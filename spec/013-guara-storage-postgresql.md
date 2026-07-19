# Spec 013: `Guara.Storage.PostgreSql` — Storage PostgreSQL

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Storage.PostgreSql`
**Implementa:** [Spec 004 (`Guara.Storage`)](004-guara-storage.md)
**Docs de referência:** [ADR-0003](../docs/adr/0003-abstracao-de-storage-por-provider.md) · [performance](../docs/performance.md)

## Problem

PostgreSQL é o backend favorito de boa parte da comunidade open-source. Ele oferece primitivas ideais para um job scheduler: `SELECT ... FOR UPDATE SKIP LOCKED` (dequeue sem contenção), advisory locks (lock distribuído) e `LISTEN/NOTIFY` (push sem broker).

## Scope

### In

- Implementação de `IStorage` sobre PostgreSQL; `UsePostgreSqlStorage(connectionString)`.
- Dequeue atômico via `FOR UPDATE SKIP LOCKED`.
- Lock distribuído via `pg_advisory_lock`.
- **Push** via `LISTEN/NOTIFY` (habilita estratégia push do Dispatcher).
- Esquema + migrations idempotentes; índices de hot path.

### Out

- Lógica de negócio; apenas persistência.

## Domain Model

- Tabelas (schema `guara`): `jobs`, `queues`, `locks`, `servers`, `recurring` (recorrentes), `calendars` (calendários — spec 038), `continuations` (vínculos pai→filho) e `state_history` (timeline de estados — opcional, `EnableStateHistory`). Visão consolidada do esquema: [Spec 004](004-guara-storage.md).
- Índice parcial para elegibilidade `(queue, state, scheduled_for, lease_until)`.
- `Capabilities`: transações `true`, lock distribuído `true` (advisory), push `true` (LISTEN/NOTIFY), server-side filter `true`.

## API Contract

```csharp
namespace Microsoft.Extensions.DependencyInjection;
public static class PostgreSqlStorageExtensions
{
    public static IGuaraBuilder UsePostgreSqlStorage(this IGuaraBuilder builder, string connectionString,
        Action<PostgreSqlStorageOptions>? configure = null);
}
```

## Authorization

Credenciais via connection string/segredos; comandos parametrizados (anti-injeção).

## Edge Cases & Failure Modes

- **Concorrência multi-nó** → `SKIP LOCKED` faz cada nó pegar linhas distintas; sem contenção nem dupla execução.
- **Lease expira** → reelegibilidade por `lease_until < now`.
- **NOTIFY perdido** (conexão caiu) → fallback para polling; nenhuma perda de job (a fonte da verdade é a tabela).
- **Advisory lock preso** (crash) → advisory locks de sessão liberam ao cair a conexão.
- **Migração concorrente** → idempotente + advisory lock.

## Non-Functional Requirements

- Dequeue sem contenção (`SKIP LOCKED`); leituras paginadas; sem N+1.
- Push reduz latência sem busy-poll.
- Parametrizado; pooling via Npgsql; AOT no hot path (EF só migrations) — DD-1.

## Integrations

PostgreSQL via `Npgsql`; `ISerializer` para payloads.

## Acceptance Criteria

- **AC-1 — Conformance kit.** Passa 100%.
- **AC-2 — SKIP LOCKED.** *Dado* K nós, *então* cada job é processado uma única vez, sem bloqueio mútuo.
- **AC-3 — Lease/visibility.** *Dado* lease expirado, *então* reelegível.
- **AC-4 — Push.** *Dado* um job novo, *então* `LISTEN/NOTIFY` acorda o Dispatcher (quando push habilitado).
- **AC-5 — Fallback.** *Dado* NOTIFY indisponível, *então* polling assume sem perder jobs.
- **AC-6 — Advisory lock.** *Dado* `TryAcquireAsync`, *então* exclusivo entre nós; libera em crash.
- **AC-7 — Migrations idempotentes.** Aplicar 2x/paralelo → esquema consistente.

## Deferred Decisions

- **DD-1 — EF Core vs Npgsql raw.** *Fallback:* migrations com EF Core; hot path com SQL/Npgsql otimizado. *Revisão:* benchmarks.
- **DD-2 — Versão mínima.** *Fallback:* PostgreSQL 13+. *Revisão:* nenhuma.
- **DD-3 — Schema.** *Fallback:* schema `guara`; configurável via `PostgreSqlStorageOptions.Schema`. *Revisão:* feedback.
- **DD-4 — AutoMigrate (resolvido 2026-07-16).** *Decisão:* `PostgreSqlStorageOptions.AutoMigrate` (default **`true`**, estilo `PrepareSchemaIfNecessary` do Hangfire) aplica as migrations idempotentes no startup, coordenadas por advisory lock; em produção recomenda-se `false` + CLI `guara migrate` (spec 027) no pipeline.

> **Implementação (2026-07-19):** provider completo — **conformance kit 100% (AC-1, 37 testes)** + 7 específicos, via Testcontainers. Aquisição com `FOR UPDATE SKIP LOCKED` em CTE + `UPDATE ... RETURNING` (AC-2/AC-3); todas as comparações temporais usam o **relógio injetado** do nó (nunca `now()` do banco) — semântica idêntica aos demais providers e testável com relógio manual. **DD-1 resolvida além do fallback:** migrations também são **raw** (DDL 100% idempotente com `IF NOT EXISTS`, sem EF) — menos dependência, AOT de ponta a ponta; aplicadas sob `pg_advisory_lock` com chave derivada do schema (AC-7, testado com boot concorrente). **Desvio consciente no AC-6:** o `ILockProvider` usa **tabela `locks` com TTL e dono** (upsert condicional decide livre/expirado/vivo atomicamente), não advisory lock de sessão — o contrato da spec 004 exige TTL/renovação/expiração testáveis; crash do dono libera pela expiração do TTL (bounded), e o advisory lock ficou onde brilha: na migração. **Push (AC-4/AC-5) pendente:** `LISTEN/NOTIFY` entra junto com a estratégia push do Dispatcher (spec 006) — até lá `SupportsServerSideTimers=false` e o polling cobre. Capabilities honestas: transações **false** por ora (operações individualmente atômicas; `ITransaction` entra quando algum motor precisar). Esquema no schema configurável (DD-3, validado por regex estrita — identificador interpolado nunca vem do usuário sem validação): `jobs`, `servers`, `locks`, `recurring`, `calendars`, `continuations`, `schema_version`. Payloads: descriptor/calendário em `jsonb` (STJ source-gen, AOT); `TimeSpan`/`TimeOnly` em ticks (round-trip exato); precisão temporal do PG é **microssegundo** (tempos são truncados de 100ns → 1µs no round-trip — irrelevante na prática, documentado). Config: `Guara:Storage:PostgreSql` (spec 018) com `UsePostgreSqlStorage()` sem argumentos.

## Open Questions

_(vazio)_
