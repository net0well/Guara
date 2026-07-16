# Spec 031: Batches — Grupos de Jobs (Pro / Comercial)

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Pro.Batches`
**Licença:** **Comercial ("Pro")** — não LGPL. Ver [Spec 035](035-governanca-licenciamento-docs.md).
**Depende de:** [Spec 001](001-guara-abstractions.md), [Spec 004](004-guara-storage.md), [Spec 008](008-guara-executor.md), [Spec 030](030-continuations.md)

## Problem

Cenários avançados agrupam muitos jobs e reagem à **conclusão do grupo como um todo**: "processe 10.000 itens **e depois** gere o consolidado". Isso vai além de continuations 1→N. Como no Hangfire (onde Batches é recurso Pro), o Guará oferece **Batches** num pacote **comercial** separado — mantendo o core OSS enxuto e habilitando o modelo de sustentação do projeto.

## Scope

### In

- Criar um **batch** (grupo) de jobs; adicionar jobs ao batch.
- **Continuation de batch**: um job/ação que dispara quando **todos** os jobs do batch atingem estado final (join/`AND`).
- Batches aninhados; continuar batch após batch.
- Estado agregado do batch (pendentes/sucesso/falha) exposto ao dashboard.

### Out

- Faz parte do tier **Pro** — não entra nos pacotes LGPL.
- Orquestração de workflow complexa (DAG completo) → futuro.

## Domain Model

- **`Batch`** — id, jobs membros, contadores agregados, estado (`Created`/`Executing`/`Completed`/`Failed`).
- **Batch continuation** — dispara ao join (todos finalizados). Complementa continuations 1→N da [Spec 030](030-continuations.md) com o caso N→1.
- Persistido via `IJobStorage` (Spec 004); join idempotente e coordenado por `ILockProvider` (Spec 025) em cluster.

## API Contract

```csharp
namespace Guara.Pro.Batches;

public interface IBatchClient // métodos em português — ADR-0010
{
    ValueTask<BatchId> CriarAsync(Action<IBatchBuilder> build, CancellationToken ct = default);
    ValueTask ContinuarBatchComAsync(BatchId batchId, JobDescriptor continuation, CancellationToken ct = default);
    ValueTask<BatchStatus> ObterStatusAsync(BatchId batchId, CancellationToken ct = default);
}
```

`AddGuaraProBatches(licenseKey)` — extensão única; valida a licença comercial.

## Authorization

Enfileiramento conforme Spec 021. Ativação exige **chave de licença** comercial válida.

## Edge Cases & Failure Modes

- **Join idempotente**: a continuação do batch dispara **uma única vez**, mesmo com retries/múltiplos nós.
- **Job do batch falha**: política configurável — continuar com sucesso parcial ou marcar batch `Failed`.
- **Batch vazio**: dispara a continuação imediatamente (join trivial).
- **Licença ausente/expirada**: o pacote **não ativa**; erro claro no startup, sem quebrar o core OSS.
- **Batch grande** (10⁵ jobs): contadores agregados sem varrer todos os jobs (agregação incremental).

## Non-Functional Requirements

- Não introduz dependência do core OSS no pacote Pro (o core não conhece Batches).
- Join idempotente/resiliente; agregação incremental (sem N+1).
- `ValueTask`, AOT-safe, multi-target `net8.0`+`net10.0`.

## Integrations

Constrói sobre continuations (Spec 030), storage (Spec 004), cluster (Spec 025); status agregado consumido pelo dashboard avançado (Spec 032).

## Acceptance Criteria

- **AC-1 — Join.** *Dado* um batch de N jobs e uma continuação de batch, *quando* os N finalizam, *então* a continuação dispara exatamente uma vez.
- **AC-2 — Idempotência.** *Dado* retries/2 nós, *então* o join não dispara em duplicidade.
- **AC-3 — Sucesso parcial.** *Dado* política "parcial" e 1 job falho, *então* a continuação ainda dispara; com política "estrita", o batch vira `Failed`.
- **AC-4 — Batch vazio.** *Dado* batch sem jobs, *então* a continuação dispara imediatamente.
- **AC-5 — Licença.** *Dado* chave inválida, *então* o pacote não ativa e o core OSS segue funcionando.
- **AC-6 — Escala.** *Dado* 100k jobs no batch, *então* os contadores agregam sem varredura linear a cada atualização.

## Deferred Decisions

- **DD-1 — Mecanismo de licenciamento.** *Fallback:* chave assinada validada offline; detalhes em [Spec 035](035-governanca-licenciamento-docs.md). *Revisão:* Spec 035.
- **DD-2 — Política de falha default.** *Fallback:* sucesso parcial (dispara continuação) — configurável. *Revisão:* feedback.
- **DD-3 — DAG completo.** *Fallback:* fora do escopo; Batches cobre join N→1, Continuations cobre 1→N. *Revisão:* pós-1.0.

## Open Questions

_(vazio)_
