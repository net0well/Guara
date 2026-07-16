# Spec 026: `Guara.Distributed` — Coordenação Distribuída

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Distributed`
**Depende de:** [Spec 001](001-guara-abstractions.md), [Spec 004](004-guara-storage.md), [Spec 025](025-guara-cluster.md)
**Docs de referência:** [components](../docs/components.md) · [performance](../docs/performance.md)

## Problem

`Guara.Cluster` (Spec 025) resolve **quem é o líder**. Cargas maiores precisam de mais: **distribuir o trabalho** entre nós (sharding de filas/partições), garantias de **entrega de eventos** entre nós e **idempotência distribuída** (dedupe de jobs disparados em duplicidade). `Guara.Distributed` cobre esses padrões avançados — opt-in, para quem escala além de um cluster simples.

## Scope

### In

- **Particionamento/sharding** de filas ou partições entre nós (afinidade de trabalho).
- **Dedupe/idempotência distribuída** (chave de idempotência por job) usando `ILockProvider`.
- **Entrega de eventos entre nós** (elevando o event bus em processo da Spec 002 para cross-node), quando um transporte estiver disponível.
- Rebalanceamento ao entrar/sair nós.

### Out

- Eleição de líder/failover (é da Spec 025).
- Execução de job (motores).
- Broker de mensageria próprio — integra com o existente (ver `dotnet-claude-kit:messaging`), não reinventa.

## Domain Model

- **Partições**: filas mapeadas a nós por uma função estável (hashing consistente) coordenada pelo líder (Spec 025).
- **Idempotência**: `IdempotencyKey` opcional no `JobDescriptor`; dedupe via lock/registro no storage.
- **Bridge de eventos**: adaptador que propaga `IGuaraEvent` entre nós quando há transporte (DD-2).

## API Contract

```csharp
namespace Guara.Distributed;

public interface IPartitionStrategy
{
    bool OwnsQueue(string queue, ClusterNode self, IReadOnlyList<ClusterNode> nodes);
}

public interface IIdempotencyGuard
{
    ValueTask<bool> TryBeginAsync(string idempotencyKey, TimeSpan window, CancellationToken ct); // false = duplicado
}
```

`AddGuaraDistributed()` habilita sharding/idempotência; **opt-in**.

## Authorization

N/A (infra). Confia no storage/lock coordenados.

## Edge Cases & Failure Modes

- **Rebalanceamento** ao entrar/sair nó → transição sem perder jobs (lease cobre a janela); coordenado pelo líder.
- **Chave de idempotência duplicada** → `TryBeginAsync` retorna false; o segundo disparo é descartado.
- **Sem transporte de eventos** → bridge desativado; cai para coordenação via storage (sem cross-node push).
- **Skew de partição** (nó lento) → lease/visibility reprocessa em outro nó.
- **Convergência** → mudanças de topologia convergem em janela documentada.

## Non-Functional Requirements

- Opt-in; **não** penaliza deploys single-node/cluster simples.
- Hashing consistente estável; rebalanceamento mínimo.
- Correção sob falha/partição de rede (idempotência + lease); skills `csharp-concurrency-patterns`/`dotnet-claude-kit:resilience`.
- AOT-safe.

## Integrations

Constrói sobre `Guara.Cluster` (Spec 025) e `ILockProvider`/`IJobStorage` (Spec 004); pode integrar a um message bus externo (`dotnet-claude-kit:messaging`) para o bridge de eventos.

## Acceptance Criteria

- **AC-1 — Sharding.** *Dado* 3 nós e filas particionadas, *então* cada fila é processada primariamente pelo nó dono, sem duplicação.
- **AC-2 — Idempotência.** *Dado* dois disparos com a mesma `IdempotencyKey` dentro da janela, *então* apenas um executa.
- **AC-3 — Rebalance.** *Dado* um nó que sai, *então* suas partições são reassumidas sem perder jobs.
- **AC-4 — Opt-in inócuo.** *Dado* `AddGuaraDistributed()` ausente, *então* o comportamento é o do cluster simples (Spec 025).
- **AC-5 — Bridge condicional.** *Dado* nenhum transporte configurado, *então* o bridge de eventos fica desativado sem erro.
- **AC-6 — Convergência.** *Dado* mudança de topologia, *então* a distribuição converge dentro da janela documentada.

## Deferred Decisions

- **DD-1 — Estratégia de partição default.** *Fallback:* hashing consistente por nome de fila. *Revisão:* testes de escala.
- **DD-2 — Transporte de eventos cross-node (resolvido).** *Decisão:* **storage-native por padrão** (Postgres `LISTEN/NOTIFY`, Redis pub/sub, Mongo change streams) — **sem broker** no stack mínimo; message bus (RabbitMQ/Azure Service Bus) é **plugin opcional**, habilitado só sob demanda comprovada de altíssimo volume. *Revisão:* pós-1.0 conforme necessidade.
- **DD-3 — Janela de idempotência default.** *Fallback:* configurável; default conservador (ex.: 24h). *Revisão:* feedback.

## Open Questions

_(vazio)_
