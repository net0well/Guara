# Spec 030: Continuations — Encadeamento de Jobs

> ⚠️ **A forma de enfileiramento nos exemplos foi substituída por [ADR-0020](../docs/adr/0020-enfileiramento-por-fabrica-tipada.md) (2026-08-06).** `ContinuarComAsync` recebe `JobDescriptor`, produzido pela fábrica que o source generator emite — não uma lambda. O comportamento de encadeamento descrito aqui continua valendo.

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Escopo:** feature transversal — `Guara.Abstractions` (contrato), `Guara.Core`/`Guara.Scheduler` (comportamento), `Guara.Storage` (persistência do vínculo)
**Licença:** OSS (core)
**Depende de:** [Spec 001](001-guara-abstractions.md), [Spec 002](002-guara-core.md), [Spec 004](004-guara-storage.md), [Spec 005](005-guara-scheduler.md), [Spec 008](008-guara-executor.md)

## Problem

Fluxos reais encadeiam trabalho: "gere o relatório **e depois** envie o e-mail". Sem continuations, o usuário teria que enfileirar manualmente o segundo job dentro do primeiro (acoplando-os) ou fazer polling. Como no Hangfire, o Guará precisa de **continuations**: um job B que dispara automaticamente quando o job A atinge um estado (por padrão, `Succeeded`).

## Scope

### In

- `IGuaraClient.ContinuarCom(paiId, jobB, options)` — registra B como continuação de A ([ADR-0010](../docs/adr/0010-api-do-usuario-em-portugues.md)).
- Gatilho por estado do pai: `OnSucceeded` (default) ou `OnAnyFinishedState` (sucesso **ou** falha).
- Persistência do vínculo pai→filhos no storage; disparo idempotente ao pai finalizar.
- Cadeias e fan-out (vários filhos de um pai); profundidade arbitrária.

### Out

- Batches/grupos com callback agregado → [Spec 031](031-batches-pro.md) (Pro).
- Grafos com múltiplos pais (join/`AND`) → futuro (DD-2).

## Domain Model

- **`ContinuationDescriptor`** — `ParentId`, `ChildJobDescriptor`, `Trigger` (`OnSucceeded`/`OnAnyFinishedState`).
- Vínculo persistido na estrutura dedicada **`Continuations`** via `IContinuationStorage` (adição extend-only à família da [Spec 004](004-guara-storage.md)); quando o pai transiciona para estado final, o `Scheduler` promove os filhos elegíveis a `Enqueued`/`Scheduled` e emite `JobScheduled`.
- Filho fica em estado `AwaitingContinuation` (novo sub-estado lógico sobre `Scheduled`) até o gatilho.

## API Contract

```csharp
public interface IGuaraClient // adição extend-only; método em português (ADR-0010)
{
    ValueTask<JobId> ContinuarComAsync(JobId paiId, JobDescriptor filho,
        ContinuationOptions? options = null, CancellationToken ct = default);
}

public sealed record ContinuationOptions(ContinuationTrigger Trigger = ContinuationTrigger.OnSucceeded);
public enum ContinuationTrigger { OnSucceeded, OnAnyFinishedState }
```

## Authorization

Mesma política de enfileiramento do `IGuaraClient` (Spec 005/021).

## Edge Cases & Failure Modes

- **Pai falha** com trigger `OnSucceeded` → filho **não** dispara; fica marcado como cancelado/descartado com motivo.
- **Pai já finalizado** ao registrar a continuação → o filho é avaliado imediatamente.
- **Disparo duplicado** (retry do pai / múltiplos nós) → promoção idempotente (via lease/lock, Spec 004/025); filho enfileira uma única vez.
- **Cadeia longa / ciclo** → detecção de ciclo; profundidade máxima configurável.
- **Pai purgado por retenção** antes do disparo → filhos órfãos tratados (disparo garantido antes da purga do pai).
- **Pai excluído** (`ExcluirAsync`) → continuações pendentes **descartadas e registradas** (visíveis no dashboard), nunca disparadas ([semantics](../docs/semantics.md)).

## Non-Functional Requirements

- Disparo idempotente e resiliente a múltiplos nós.
- `ValueTask`; sem reflection; AOT-safe.
- Persistência do vínculo sem N+1 ao promover fan-out.

## Integrations

Usa `IJobStorage` (vínculo), `IScheduler` (promoção), eventos `JobCompleted`/`JobFailed` (Spec 001).

## Acceptance Criteria

- **AC-1 — Sucesso dispara filho.** *Dado* B como continuação de A (`OnSucceeded`), *quando* A conclui com sucesso, *então* B é enfileirado automaticamente.
- **AC-2 — Falha não dispara (OnSucceeded).** *Dado* trigger `OnSucceeded`, *quando* A falha, *então* B não roda e fica registrado como descartado.
- **AC-3 — OnAnyFinishedState.** *Dado* trigger `OnAnyFinishedState`, *quando* A falha, *então* B ainda dispara.
- **AC-4 — Fan-out.** *Dado* A com 3 continuações, *então* as 3 disparam ao A concluir.
- **AC-5 — Idempotência.** *Dado* re-execução/retry de A ou disparo em 2 nós, *então* cada filho enfileira exatamente uma vez.
- **AC-6 — Pai já concluído.** *Dado* A já `Succeeded`, *quando* registro B, *então* B enfileira imediatamente.
- **AC-7 — Ciclo.** *Dada* uma cadeia que formaria ciclo, *então* é rejeitada com erro claro.

## Deferred Decisions

- **DD-1 — Sub-estado `AwaitingContinuation`.** *Fallback:* modelado sobre `Scheduled` com flag; não altera o enum `JobState` da Spec 001. *Revisão:* implementação.
- **DD-2 — Join de múltiplos pais (`AND`).** *Fallback:* fora do 1.0; continuação é 1-pai→N-filhos. *Revisão:* pós-1.0 (relaciona a Batches, Spec 031).
- **DD-3 — Profundidade máxima.** *Fallback:* configurável, default generoso (ex.: 100). *Revisão:* feedback.

> **Implementação (2026-07-18):** entregue conforme o contrato (`ContinuarComAsync(paiId, filho, ContinuationOptions?, ct)` → `JobId` do filho; `ContinuationOptions`/`ContinuationTrigger` em `Guara.Abstractions`). **DD-1 confirmada:** o filho aguarda como job comum em `Scheduled` **sem** `ScheduledFor` (nunca elegível) — sem estado novo no enum; o vínculo vive na estrutura `Continuations` (`IContinuationStorage`, spec 004). **Promoção** é do Scheduler (`ContinuationPromoter`): handlers de `JobCompleted`/`JobFailed` avaliam os vínculos do pai — gatilho atendido enfileira o filho; não atendido **descarta a cadeia inteira em cascata** (job do filho excluído; vínculo permanece como registro com o motivo — AC-2). **Idempotência (AC-5)** vem do `TryResolveAsync` (resolve uma única vez a partir de `Pending`; a ordem "filho primeiro, resolução depois" faz repetições serem inócuas). **Recuperação:** varredura na manutenção do servidor (`SweepAsync`, spec 010) resolve pendências cujo pai finalizou sem promover (queda no meio) ou sumiu — e roda **antes** da purga, garantindo o disparo antes do pai ser removido pela retenção. Registro com pai já finalizado reavalia na hora (AC-6); pai excluído via `ExcluirAsync` descarta a cadeia pendente na hora (semantics). **AC-7 (ciclo) vale por construção:** o filho é sempre um job novo — o grafo é acíclico por definição; o que existe é o teto de profundidade (DD-3: constante 100 por ora; vira opção quando o binding de configuração chegar, spec 018). Como `JobFailed` só dispara na falha definitiva (retentativa persistente, spec 008), `Retrying` não é estado final e o filho espera (semantics). Fora deste incremento: `Executa(() => ...)` tipado (source generator, specs 019/029).

## Open Questions

_(vazio)_
