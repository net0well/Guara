# Spec 036: Atributos de Job — Comportamento Declarativo

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Escopo:** feature transversal — atributos em `Guara.Abstractions`; comportamento realizado por `Guara.SourceGenerators` (leitura em compilação), pipeline (`Guara.Core`/`Guara.Executor`), `Guara.Scheduler` e `ILockProvider` (`Guara.Storage`)
**Licença:** OSS (core)
**Depende de:** [Spec 002](002-guara-core.md), [Spec 004](004-guara-storage.md), [Spec 005](005-guara-scheduler.md), [Spec 008](008-guara-executor.md), [Spec 029](029-guara-sourcegenerators.md)
**Docs de referência:** [ADR-0005](../docs/adr/0005-source-generators-para-registro.md) · [ADR-0010](../docs/adr/0010-api-do-usuario-em-portugues.md)

## Problem

Comportamentos por job — fila, política de retentativa, exclusão mútua, tempo limite — precisam ser declarados **no próprio job**, de forma visível e autodocumentada, como os filter attributes do Hangfire (`[DisableConcurrentExecution]`, `[AutomaticRetry]`, `[Queue]`). Sem isso, o usuário teria que configurar cada job por código de setup, longe de onde o job vive.

Sendo API do usuário, os atributos seguem o [ADR-0010](../docs/adr/0010-api-do-usuario-em-portugues.md): **nomes em português, prefixados `Guara`**.

## Scope

### In

- **`[GuaraFila("nome")]`** — define a fila do job (default: `"default"`).
- **`[GuaraRetentativas(maximo)]`** — política de retentativa por job; `0` = nunca retentar (efeito colateral irreversível).
- **`[GuaraDesabilitarConcorrencia]`** — exclusão mútua: no máximo **uma execução simultânea** do job (por chave), mesmo entre nós — equivalente ao `[DisableConcurrentExecution]` do Hangfire, via `ILockProvider`.
- **`[GuaraTempoLimite(segundos)]`** — tempo máximo de execução; excedido → cancelamento cooperativo → `Failed` (realiza a DD-3 da [Spec 008](008-guara-executor.md)).
- **`[GuaraPularSeAnteriorEmExecucao]`** — para **recorrentes**: se a ocorrência anterior ainda roda, a nova é pulada (não acumula).
- Leitura dos atributos **em compilação** pelo `Guara.SourceGenerators` → metadados no registry gerado (zero reflection em runtime, [ADR-0005](../docs/adr/0005-source-generators-para-registro.md)).

### Out

- `[GuaraJob]` — é o **marcador de descoberta** ([Spec 029](029-guara-sourcegenerators.md)), não um atributo de comportamento; permanece lá.
- Filtros programáticos globais (equivalente a `IJobFilter` global) → middlewares custom no slot `Custom` do pipeline ([Spec 002](002-guara-core.md)) já cobrem.
- Atributos do tier Pro (batches) → [Spec 031](031-batches-pro.md).

## Domain Model

| Atributo | Alvo | Realizado por | Semântica |
|---|---|---|---|
| `[GuaraFila]` | método/classe | `IGuaraClient`/Scheduler ao criar o `JobRecord` | `Queue` do job |
| `[GuaraRetentativas]` | método/classe | `RetryMiddleware` (Core) via metadados do registry | Sobrescreve `RetryOptions.MaxAttempts` para o job |
| `[GuaraDesabilitarConcorrencia]` | método/classe | Middleware de mutex (slot `Custom` interno) sobre `ILockProvider` | Lock por chave antes de executar; ocupado → re-enfileira (não bloqueia worker) |
| `[GuaraTempoLimite]` | método/classe | `Executor` (CTS linkado) | Cancela cooperativamente ao exceder |
| `[GuaraPularSeAnteriorEmExecucao]` | método/classe (recorrente) | `Scheduler` ao promover a ocorrência | Ocorrência pulada se a anterior não finalizou |

- **Chave de concorrência default**: `"{tipo}.{metodo}"`; customizável via `Chave` (suporta placeholders de argumento: `"cliente-{0}"`).
- Precedência: atributo no **método** vence o da **classe**; ambos vencem o default global.
- Os valores viajam como **metadados do job** no registry gerado/`JobDescriptor.Metadata` — nenhum `GetCustomAttributes` em runtime.

## API Contract

```csharp
namespace Guara.Abstractions; // atributos são API do usuário — português (ADR-0010)

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class GuaraFilaAttribute(string nome) : Attribute
{
    public string Nome { get; } = nome;
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class GuaraRetentativasAttribute(int maximo) : Attribute
{
    public int Maximo { get; } = maximo;          // 0 = nunca retentar
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class GuaraDesabilitarConcorrenciaAttribute : Attribute
{
    public string? Chave { get; init; }           // default: "{tipo}.{metodo}"; aceita "{0}", "{1}"...
    public int EsperaSegundos { get; init; }      // 0 = re-enfileira imediatamente se ocupado
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class GuaraTempoLimiteAttribute(int segundos) : Attribute
{
    public int Segundos { get; } = segundos;
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class GuaraPularSeAnteriorEmExecucaoAttribute : Attribute;
```

Uso:

```csharp
public sealed class RelatorioJobs(IRelatorioService servico)
{
    [GuaraJob]
    [GuaraFila("relatorios")]
    [GuaraRetentativas(5)]
    [GuaraDesabilitarConcorrencia(Chave = "relatorio-{0}")]
    [GuaraTempoLimite(300)]
    public Task GerarAsync(int clienteId, CancellationToken ct) => servico.GerarAsync(clienteId, ct);
}
```

## Authorization

N/A (declarativo). O lock de concorrência usa o `ILockProvider` do storage configurado.

## Edge Cases & Failure Modes

- **Lock de concorrência ocupado** → o job **volta à fila** (re-enfileirado com pequeno atraso), o worker segue livre; com `EsperaSegundos > 0`, aguarda até esse limite antes de re-enfileirar. Nunca executa em dobro.
- **Nó morre segurando o lock** → TTL do lock expira (fail-safe do `ILockProvider`, spec 004).
- **Storage sem lock distribuído** (`SupportsDistributedLock=false`, ex.: Memory) → mutex vale por processo; documentado e refletido em `Capabilities`.
- **Placeholder de chave inválido** (`{9}` sem argumento) → **erro de compilação** via source generator/analyzer, não erro em runtime.
- **`[GuaraTempoLimite]` excedido** → cancelamento **cooperativo** (job deve honrar o `CancellationToken`). Job que honra o token → `Failed` com motivo "tempo limite". Job que **ignora** o token e completa → **`Succeeded` + aviso no log** — o estado reflete a realidade (o efeito já ocorreu); o slot fica ocupado até o fim (decisão 2026-07-17, [semantics](../docs/semantics.md)).
- **`[GuaraPularSeAnteriorEmExecucao]`** → ocorrência pulada é registrada (visível no dashboard), não silenciosa. **Sem o atributo, ocorrências de recorrentes sobrepõem por padrão** (modelo Quartz/Hangfire — decisão 2026-07-17, [semantics](../docs/semantics.md)).
- **Atributo em método e classe** → método vence (documentado).

## Non-Functional Requirements

- **Zero reflection**: atributos lidos em **compilação** (source generator) e materializados como metadados no registry gerado — AOT/trimming-safe ([ADR-0005](../docs/adr/0005-source-generators-para-registro.md)).
- Mutex de concorrência de baixo overhead: um `TryAcquireAsync` por execução; TTL = lease do job.
- Extend-only: novos atributos entram sem quebrar os existentes.

## Integrations

Source generator (Spec 029) lê e emite metadados; `RetryMiddleware` (Spec 002), `Executor` (Spec 008), `Scheduler` (Spec 005) e `ILockProvider` (Spec 004) honram os valores. O dashboard (Spec 022/032) exibe fila/retentativas/ocorrências puladas.

## Acceptance Criteria

- **AC-1 — Fila.** *Dado* `[GuaraFila("relatorios")]`, *quando* o job é enfileirado, *então* seu `JobRecord.Queue == "relatorios"`.
- **AC-2 — Retentativas.** *Dado* `[GuaraRetentativas(0)]`, *quando* o job falha, *então* vira `Failed` sem retentativa; com `(5)`, retenta até 5 vezes.
- **AC-3 — Concorrência entre nós.** *Dado* `[GuaraDesabilitarConcorrencia]` e 2 nós disparando o mesmo job/chave, *então* no máximo um executa por vez; o outro é re-enfileirado.
- **AC-4 — Chave por argumento.** *Dado* `Chave = "cliente-{0}"`, *então* jobs de clientes **diferentes** executam em paralelo; do **mesmo** cliente, nunca.
- **AC-5 — Tempo limite.** *Dado* `[GuaraTempoLimite(1)]` e um job de 10s que honra o token, *então* é cancelado e vira `Failed` com motivo de tempo limite.
- **AC-6 — Pular anterior.** *Dado* um recorrente com `[GuaraPularSeAnteriorEmExecucao]` cuja execução anterior ainda roda, *então* a nova ocorrência é pulada e registrada.
- **AC-7 — Zero reflection.** *Dado* `PublishAot=true`, *então* a leitura dos atributos não usa reflection em runtime (metadados vêm do registry gerado).
- **AC-8 — Precedência.** *Dado* atributo na classe e no método, *então* o do método vence.

## Deferred Decisions

- **DD-1 — Comportamento default quando lock ocupado.** *Fallback:* re-enfileirar com atraso curto (back-off) — não bloqueia o worker; `EsperaSegundos` opcional para espera limitada. *Revisão:* feedback de produção.
- **DD-2 — Placeholders na chave.** *Fallback:* índices de argumento (`{0}`, `{1}`) validados em compilação. *Revisão:* Spec 029 (implementação do generator).
- **DD-3 — Atributos futuros** (ex.: expiração de resultado por job, prioridade). *Fallback:* fora do 1.0; entram extend-only. *Revisão:* pós-1.0.

## Open Questions

_(vazio)_
