# Spec 011: `Guara.Storage.Memory` — Storage em Memória

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Storage.Memory`
**Implementa:** [Spec 004 (`Guara.Storage`)](004-guara-storage.md)
**Docs de referência:** [ADR-0003](../docs/adr/0003-abstracao-de-storage-por-provider.md) · [performance](../docs/performance.md)

## Problem

Desenvolvimento, testes e demos precisam de um storage **sem dependência externa**, rápido e determinístico. `Guara.Storage.Memory` implementa `IStorage` inteiramente em memória, passando o mesmo conformance kit dos providers persistentes — para que o comportamento observado localmente reflita produção (menos surpresas para quem adota o framework).

## Scope

### In

- Implementação de `IStorage`/`IJobStorage`/`IQueueStorage`/`ILockProvider`/`ITransaction` em memória.
- `Use...Storage()` (extensão única): `UseMemoryStorage()`.
- Semântica **single-process**: aquisição atômica, lease/visibility via `TimeProvider`.

### Out

- Durabilidade (dados perdidos ao reiniciar — documentado).
- Lock **distribuído** real (é process-local; `Capabilities` declara isso).

## Domain Model

- Estruturas concorrentes (`ConcurrentDictionary`, filas) protegendo `JobRecord` por fila.
- `Capabilities`: `SupportsTransactions=true` (process-local), `SupportsDistributedLock=false`, `SupportsServerSideFilter=true`, `SupportsServerSideTimers=false`.

## API Contract

```csharp
namespace Microsoft.Extensions.DependencyInjection;
public static class MemoryStorageExtensions
{
    public static IGuaraBuilder UseMemoryStorage(this IGuaraBuilder builder);
}
```

## Authorization

N/A.

## Edge Cases & Failure Modes

- **Reinício do processo** → estado perdido (esperado; documentado).
- **Aquisição concorrente** → atômica via operações lock-free/`ConcurrentDictionary`; nunca dois obtêm o mesmo job.
- **Lease expira** → job volta a ser elegível (relógio via `TimeProvider`).
- **Crescimento ilimitado** → retenção/purga aplicável; opção de capacidade máxima (DD-1).

## Non-Functional Requirements

- Baixíssima latência; sem I/O.
- Thread-safe; sem locks grosseiros ([performance](../docs/performance.md)).
- AOT-safe.

## Integrations

Nenhuma externa; usa `ISerializer` (Spec 003) como os demais para paridade de comportamento.

## Acceptance Criteria

- **AC-1 — Conformance kit.** *Dado* o kit da Spec 004, *então* o provider passa 100%.
- **AC-2 — Atômico.** *Dado* concorrência alta, *então* nenhum job é adquirido por dois consumidores.
- **AC-3 — Lease.** *Dado* lease expirado sem renovação, *então* o job reaparece elegível.
- **AC-4 — Capabilities honestas.** *Dado* `Capabilities`, *então* `SupportsDistributedLock=false`.
- **AC-5 — AOT.** *Dado* `PublishAot=true`, *então* funciona sem warnings.
- **AC-6 — Não durável documentado.** *Dado* reinício, *então* o comportamento (perda de dados) está documentado e testado.

## Deferred Decisions

- **DD-1 — Capacidade máxima/eviction.** *Fallback:* ilimitado no MVP; opção de limite depois. *Revisão:* pós-MVP.

## Open Questions

_(vazio)_
