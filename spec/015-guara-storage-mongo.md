# Spec 015: `Guara.Storage.Mongo` — Storage MongoDB

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Storage.Mongo`
**Implementa:** [Spec 004 (`Guara.Storage`)](004-guara-storage.md)
**Docs de referência:** [ADR-0003](../docs/adr/0003-abstracao-de-storage-por-provider.md) · [performance](../docs/performance.md)

## Problem

MongoDB é comum em stacks orientadas a documentos. Ele oferece `findAndModify` (dequeue atômico), índices **TTL** (retenção automática) e **change streams** (push). Precisamos implementar `IStorage` aproveitando essas primitivas e declarando as capacidades reais (transações exigem replica set).

## Scope

### In

- Implementação de `IStorage` sobre MongoDB; `UseMongoStorage(connectionString)`.
- Dequeue atômico via `findAndModify` (`FindOneAndUpdate` com filtro de elegibilidade + set de lease).
- **Retenção** via índice TTL em jobs concluídos.
- **Push** via change streams (habilita estratégia push do Dispatcher).
- `ILockProvider` via coleção de locks com TTL.

### Out

- Lógica de negócio.

## Domain Model

- Coleções: `jobs`, `queues`, `locks`, `servers`, `recurring` (recorrentes), `continuations` (vínculos pai→filho) e `state_history` (timeline — opcional, `EnableStateHistory`, com TTL próprio). Visão consolidada: [Spec 004](004-guara-storage.md). `MongoStorageOptions.AutoMigrate` (default `true`) = criação **idempotente** de coleções/índices no startup; em produção, `false` + CLI `guara migrate`.
- Índices: elegibilidade `(queue, state, scheduledFor, leaseUntil)`; TTL em `completedAt` para purga.
- `Capabilities`: transações `true` (replica set) / `false` (standalone), lock distribuído `true` (coleção+TTL), push `true` (change streams), server-side filter `true`.

## API Contract

```csharp
namespace Microsoft.Extensions.DependencyInjection;
public static class MongoStorageExtensions
{
    public static IGuaraBuilder UseMongoStorage(this IGuaraBuilder builder, string connectionString,
        Action<MongoStorageOptions>? configure = null);
}
```

## Authorization

Credenciais/TLS via configuração; filtros do servidor não aceitam expressões do payload (anti-injeção de operadores).

## Edge Cases & Failure Modes

- **Concorrência** → `FindOneAndUpdate` atômico garante um único vencedor por job.
- **Lease expira** → filtro `leaseUntil < now` reelege.
- **TTL de retenção** → jobs concluídos removidos automaticamente; TTL do Mongo tem granularidade de ~1min (documentado).
- **Transações** → só com replica set; standalone declara `SupportsTransactions=false` e usa operações atômicas equivalentes.
- **Change stream** exige replica set; sem ele, cai para polling.

## Non-Functional Requirements

- Dequeue atômico indexado; leituras paginadas; sem N+1.
- Push reduz latência; AOT-safe (MongoDB.Driver); thread-safe.

## Integrations

MongoDB via `MongoDB.Driver`; `ISerializer` para payloads (ou BSON nativo — DD-2).

## Acceptance Criteria

- **AC-1 — Conformance kit.** Passa 100% (perfil conforme replica set/standalone).
- **AC-2 — findAndModify atômico.** *Dado* K nós, *então* cada job é processado uma vez.
- **AC-3 — Lease/visibility.** *Dado* lease expirado, *então* reelegível.
- **AC-4 — TTL retenção.** *Dado* job concluído além da retenção, *então* removido automaticamente pelo TTL.
- **AC-5 — Change stream push.** *Dado* replica set, *então* um job novo acorda o Dispatcher.
- **AC-6 — Capabilities honestas.** *Dado* standalone, *então* `SupportsTransactions=false` e o comportamento é atômico equivalente.

## Deferred Decisions

- **DD-1 — Transações vs atômico.** *Fallback:* usar `findAndModify` atômico como base; transações só quando `Capabilities` permitir. *Revisão:* nenhuma.
- **DD-2 — BSON nativo vs `ISerializer`.** *Fallback:* payload de args via `ISerializer` (paridade); metadados como documento BSON nativo. *Revisão:* benchmarks.
- **DD-3 — Versão/topologia mínima.** *Fallback:* MongoDB 5.0+, replica set recomendado para push/transações. *Revisão:* nenhuma.

## Open Questions

_(vazio)_
