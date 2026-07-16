# Spec 025: `Guara.Cluster` — Cluster (Leader Election, Failover)

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Cluster`
**Depende de:** [Spec 001](001-guara-abstractions.md), [Spec 004](004-guara-storage.md), [Spec 010](010-guara-server.md)
**Docs de referência:** [components](../docs/components.md) · [ADR-0002](../docs/adr/0002-comunicacao-por-eventos.md) · [performance](../docs/performance.md)

## Problem

Em produção o Guará roda em **vários nós** (para HA e escala). Sem coordenação, todos tentariam recomputar recurring, rodar manutenção e agendar — duplicando trabalho. `Guara.Cluster` fornece **eleição de líder, heartbeat, descoberta de nós, failover e locks distribuídos**, usando o `ILockProvider`/storage como fonte de verdade — sem exigir um serviço externo de coordenação.

## Scope

### In

- **Leader election** sobre `ILockProvider` (lease de liderança com TTL renovável).
- **Heartbeat/descoberta de nós** (quem está vivo) via storage (Spec 010).
- **Failover**: se o líder morre (lease expira), outro nó assume.
- **Locks distribuídos** de alto nível para seções críticas (manutenção, recompute de recurring).
- Contrato para consultar "sou líder?" e reagir a mudança de papel.

### Out

- Execução de jobs (é dos motores); o cluster só coordena **quem faz o quê**.
- Transporte/protocolo próprio de rede — coordenação é via storage/lock (sem gossip próprio no MVP).

## Domain Model

- **`IClusterCoordinator`** — `IsLeader`, evento `LeadershipChanged`, `TryBecomeLeaderAsync`.
- **`ClusterNode`** — id, endpoint, papel, último heartbeat (compartilha modelo com `ServerNode` da Spec 010).
- Liderança = posse de um lock nomeado (`guara:leader`) com TTL; renovação mantém; expiração dispara eleição.

## API Contract

```csharp
namespace Guara.Cluster;

public interface IClusterCoordinator
{
    bool IsLeader { get; }
    event Action<bool> LeadershipChanged; // true quando vira líder
    ValueTask StartAsync(CancellationToken ct);
    ValueTask StopAsync(CancellationToken ct);
}
```

`AddGuaraCluster()` registra o coordenador; o `Guara.Server` (Spec 010) consulta `IsLeader` para gating.

## Authorization

N/A (coordenação interna). Depende de acesso confiável ao storage/lock.

## Edge Cases & Failure Modes

- **Split-brain** → mitigado por lock com TTL na fonte única (storage); dois líderes só possíveis por curto skew de relógio — operações críticas revalidam a posse do lock antes de agir.
- **Líder morre** → lease expira; outro nó assume dentro de ~TTL; jobs em execução são cobertos por lease/visibility (Spec 004).
- **Relógios dessincronizados** → TTLs com margem; usar `TimeProvider`; revalidação antes de seção crítica.
- **Storage sem lock distribuído** (`Capabilities.SupportsDistributedLock=false`, ex.: Memory) → cluster degrada para **single-node** (avisa; não elege).
- **Renovação falha** → nó cede liderança imediatamente (fail-safe).

## Non-Functional Requirements

- Coordenação de baixo overhead; renovação de lease barata.
- Correção sob concorrência/falha (revalidação antes de seções críticas) — skills `csharp-concurrency-patterns`/`dotnet-claude-kit:resilience`.
- AOT-safe; sem dependência externa obrigatória.

## Integrations

Usa `ILockProvider`/`IJobStorage` (Spec 004); consumido por `Guara.Server` (Spec 010) para gating de scheduling/manutenção.

## Acceptance Criteria

- **AC-1 — Um líder.** *Dado* N nós, *então* no máximo um se considera líder a cada instante (revalidado antes de agir).
- **AC-2 — Failover.** *Dado* o líder derrubado, *então* outro nó assume dentro de ~TTL e o trabalho de líder continua.
- **AC-3 — Gating.** *Dado* um nó não-líder, *então* ele não recomputa recurring nem roda manutenção global.
- **AC-4 — Degradação single-node.** *Dado* storage sem lock distribuído, *então* o cluster opera como nó único e avisa.
- **AC-5 — Fail-safe na renovação.** *Dado* falha ao renovar o lease de liderança, *então* o nó cede a liderança imediatamente.
- **AC-6 — Sem duplicação.** *Dado* failover, *então* recurring/manutenção não são executados em duplicidade além da janela de segurança documentada.

## Deferred Decisions

- **DD-1 — TTL de liderança.** *Fallback:* 30s com renovação a cada 10s. *Revisão:* testes de caos.
- **DD-2 — Descoberta via gossip/rede.** *Fallback:* descoberta via storage (sem protocolo próprio); gossip é pós-MVP se necessário. *Revisão:* pós-MVP.
- **DD-3 — Particionamento de trabalho** (sharding de filas entre nós). *Fallback:* todos os nós processam todas as filas (concorrência controlada por lease); sharding é pós-MVP. *Revisão:* Spec 026.

## Open Questions

_(vazio)_
