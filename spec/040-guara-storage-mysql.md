# Spec 040: `Guara.Storage.MySql` — Storage MySQL

**Status:** Draft (2026-07-19)
**Date:** 2026-07-19
**Componente:** `Guara.Storage.MySql`
**Implementa:** [Spec 004 (`Guara.Storage`)](004-guara-storage.md)
**Docs de referência:** [ADR-0003](../docs/adr/0003-abstracao-de-storage-por-provider.md) · [Spec 013 (blueprint PostgreSQL)](013-guara-storage-postgresql.md)

## Problem

MySQL/MariaDB é onipresente em hospedagens e times PHP/legado migrando para .NET. A partir do MySQL 8.0, `SELECT ... FOR UPDATE SKIP LOCKED` existe — o mesmo desenho de dequeue sem contenção do provider PostgreSQL se aplica quase 1:1.

## Scope

### In

- Implementação de `IStorage` sobre MySQL 8+; `UseMySqlStorage(connectionString)` + seção `Guara:Storage:MySql` (spec 018).
- Dequeue atômico via `FOR UPDATE SKIP LOCKED`; locks com TTL em tabela (dono + expiração, relógio injetado — contrato do conformance kit).
- Esquema idempotente com `AutoMigrate` coordenado por `GET_LOCK`/`RELEASE_LOCK`.
- Estruturas completas: `jobs`, `servers`, `locks`, `recurring`, `calendars`, `continuations`, `schema_version` (espelho da spec 013; payloads em colunas `JSON`).

### Out

- Suporte a MySQL < 8.0 / MariaDB sem SKIP LOCKED (documentar requisito mínimo).
- Push server-side (MySQL não tem LISTEN/NOTIFY; polling é a estratégia).

## Acceptance Criteria

- **AC-1 — Conformance kit.** Herda `StorageConformanceTests` (tests/Guara.Storage.Conformance) e passa 100% via Testcontainers.
- **AC-2 — SKIP LOCKED.** K nós concorrentes: cada job processado uma única vez.
- **AC-3 — Migrations idempotentes.** Aplicar 2x/em paralelo → esquema consistente (GET_LOCK).
- **AC-4 — Driver isolado.** Dependência (MySqlConnector, MIT) confinada a este pacote (ADR-0009).

## Deferred Decisions

- **DD-1 — Driver.** *Fallback:* `MySqlConnector` (MIT, async de verdade) em vez de `MySql.Data` (GPL/Oracle). *Revisão:* implementação.
- **DD-2 — Precisão temporal.** *Fallback:* `DATETIME(6)` (microssegundo, como o PG). *Revisão:* implementação.
- **DD-3 — Ordem na Fase F.** *Decisão do autor (2026-07-19):* SQL Server (012) → **MySQL (040)** → MongoDB (015) → Redis (014, re-escopado).

## Open Questions

_(vazio)_
