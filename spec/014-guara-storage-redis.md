# Spec 014: `Guara.Storage.Redis` — Storage Redis

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Storage.Redis`
**Implementa:** [Spec 004 (`Guara.Storage`)](004-guara-storage.md)
**Docs de referência:** [ADR-0003](../docs/adr/0003-abstracao-de-storage-por-provider.md) · [performance](../docs/performance.md)

## Problem

Redis oferece latência muito baixa e lock distribuído nativo, atraente para filas de alta vazão. Precisamos implementar `IStorage` com **atomicidade via scripts Lua**, agendamento por **sorted sets** (score = timestamp) e locks com **TTL** — declarando honestamente as limitações (consultas ricas para o Dashboard são limitadas; durabilidade depende da configuração do Redis).

## Scope

### In

- Implementação de `IStorage` sobre Redis; `UseRedisStorage(configuration)`.
- Dequeue atômico via **script Lua** (mover de "ready" para "processing" com lease).
- Agendados em **sorted set** (score = `scheduledFor`); promoção ao vencer.
- `ILockProvider` com **TTL** (SET NX PX + token; release por Lua/compare-and-del).
- Push opcional via keyspace notifications (DD-2).

### Out

- Consultas relacionais ricas do Dashboard (suporte reduzido — documentado).
- Lógica de negócio.

## Domain Model

- Chaves: `guara:{queue}:ready` (lista/stream), `guara:{queue}:processing` (com lease), `guara:scheduled` (zset), `guara:job:{id}` (hash), `guara:lock:{key}`, `guara:recurring` (zset por próximo disparo + hash de definição), `guara:calendars` (hash — spec 038), `guara:continuations:{parentId}` (set de filhos) e `guara:job:{id}:history` (lista aparada — timeline opcional, `EnableStateHistory`). Visão consolidada: [Spec 004](004-guara-storage.md). *AutoMigrate não se aplica (schemaless).*
- `Capabilities`: transações `false` (usa Lua atômico), lock distribuído `true`, server-side filter `false` (limitado), server-side timers `false`.

## API Contract

```csharp
namespace Microsoft.Extensions.DependencyInjection;
public static class RedisStorageExtensions
{
    public static IGuaraBuilder UseRedisStorage(this IGuaraBuilder builder, string configuration,
        Action<RedisStorageOptions>? configure = null);
}
```

## Authorization

Credenciais/TLS via configuração; sem exposição de comandos crus ao payload.

## Edge Cases & Failure Modes

- **Atomicidade** → scripts Lua garantem mover job de ready→processing + set lease numa única operação.
- **Lease expira** → job em `processing` com lease vencido é devolvido a `ready` por varredura/registro.
- **Durabilidade** → depende de AOF/RDB; comportamento (possível perda em crash sem AOF) **documentado** e refletido no conformance kit (marcado como não-durável se assim configurado).
- **Lock distribuído** → single-instance por padrão; RedLock multi-nó opcional (DD-1).
- **Big payloads** → limite configurável; acima disso, erro claro.

## Non-Functional Requirements

- Latência mínima; operações O(log n) no zset.
- Atomicidade sem locks de aplicação (Lua).
- AOT-safe (StackExchange.Redis); thread-safe.

## Integrations

Redis via `StackExchange.Redis`; `ISerializer` para payloads.

## Acceptance Criteria

- **AC-1 — Conformance kit.** Passa 100% (com perfil de durabilidade declarado).
- **AC-2 — Dequeue atômico.** *Dado* K consumidores, *então* cada job é processado uma vez (Lua atômico).
- **AC-3 — Agendamento.** *Dado* um job com `scheduledFor` futuro, *então* só fica elegível ao vencer (promoção do zset).
- **AC-4 — Lease.** *Dado* lease expirado em `processing`, *então* o job retorna a `ready`.
- **AC-5 — Lock TTL.** *Dado* `TryAcquireAsync`, *então* o lock expira sozinho pelo TTL; release é seguro (compare-and-del).
- **AC-6 — Capabilities honestas.** *Dado* `Capabilities`, *então* `SupportsServerSideFilter=false` e o Dashboard usa as consultas suportadas.

## Deferred Decisions

- **DD-1 — RedLock multi-nó.** *Fallback:* lock single-instance no MVP; RedLock opcional depois. *Revisão:* Spec 025 (Cluster).
- **DD-2 — Push por keyspace notifications.** *Fallback:* polling; push opcional se o servidor habilitar notifications. *Revisão:* pós-MVP.
- **DD-3 — Streams vs Lists.** *Fallback:* Streams (consumer groups) para filas; avaliar Lists para casos simples. *Revisão:* benchmarks.

## Open Questions

_(vazio)_
