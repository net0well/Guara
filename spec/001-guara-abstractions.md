# Spec 001: `Guara.Abstractions` — Contratos, Eventos e Tipos-Base

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Abstractions`
**Docs de referência:** [ARCHITECTURE](../docs/ARCHITECTURE.md) · [components](../docs/components.md) · [dependency-rules](../docs/dependency-rules.md) · [naming-conventions](../docs/naming-conventions.md) · [execution-flows](../docs/execution-flows.md) · [performance](../docs/performance.md) · [ADR-0001](../docs/adr/0001-arquitetura-orientada-a-componentes.md)

## Problem

Num framework orientado a componentes, cada componente só pode conhecer os outros por **contrato**. Se cada pacote definisse seus próprios tipos compartilhados, surgiriam dependências cruzadas e ciclos — exatamente o que a arquitetura proíbe. É preciso **um único pacote-folha**, sem dependências, que concentre o vocabulário comum (interfaces, eventos, value types, abstrações de pipeline) referenciado por todos.

Quem depende disso:
- **Componentes internos** do Guará (motores, hosting, providers).
- **Autores de providers/extensões de terceiros** (quem escreve um `IJobStorage` ou middleware customizado) — audiência **first-class** desde a v1.

Sucesso = superfície **mínima, estável, imutável e AOT-safe**, que raramente quebra e nunca arrasta uma dependência para dentro do ecossistema.

## Scope

### In

- **Contratos (interfaces)** dos motores e serviços transversais que atravessam componentes: `IScheduler`, `IDispatcher`, `IWorker`, `IExecutor`, `ILeaderElection`, `IEventPublisher`, `IEventHandler<TEvent>`, `IGuaraBuilder`.
- **Value types centrais** (vocabulário): `JobId`, `JobState`, `JobDescriptor`, `ScheduleDescriptor`.
- **Eventos** do fluxo entre componentes (`IGuaraEvent` + registros no passado).
- **Abstrações do pipeline**: `IJobContext`, `JobDelegate`, `IJobMiddleware`.
- **Contratos de opções**: marcador/base para `*Options` validáveis.
- **Regras transversais** do pacote (nomenclatura, estilo assíncrono, AOT, compatibilidade).

### Out

- Qualquer **implementação** ou lógica de negócio (fica em Core/componentes).
- Qualquer **extensão `AddGuara...()`** ou tipo no namespace `Microsoft.Extensions.DependencyInjection` (cada pacote expõe a sua — [ADR-0006](../docs/adr/0006-uma-extensao-addguara-por-pacote.md)).
- **Contratos da família Storage** (`IStorage`, `IJobStorage`, `IQueueStorage`, `ITransaction`, `ILockProvider`) → vivem em **`Guara.Storage`** (pacote de contratos), conforme `Filosofia.md`. Ver DD-4.
- **Abstrações de logging/métricas próprias** → reutilizar BCL (`Microsoft.Extensions.Logging`, `System.Diagnostics.Metrics`). Ver DD-3.
- **Comportamento** de cada contrato (assinaturas de membros e semântica) → especificado na spec do componente **dono** (ver tabela em Domain Model).
- Serialização concreta, entrega/ordenação de eventos, máquina de estados concreta.

**Linha do MVP:** este pacote inteiro é MVP — nada aqui é adiável, pois tudo o mais depende dele.

## Domain Model

`Guara.Abstractions` **cataloga** contratos, mas o **corpo (membros + semântica)** de cada contrato pertence à spec do componente que o realiza. Isto evita duplicação e mantém "um componente dono por comportamento".

| Tipo | Categoria | Corpo definido em |
|---|---|---|
| `IScheduler` | contrato de motor | Spec 005 (`Guara.Scheduler`) |
| `IDispatcher` | contrato de motor | Spec 006 (`Guara.Dispatcher`) |
| `IWorker` | contrato de motor | Spec 007 (`Guara.Worker`) |
| `IExecutor` | contrato de motor | Spec 008 (`Guara.Executor`) |
| `ILeaderElection`, `ILeadership` | coordenação entre nós | Spec 025 (`Guara.Cluster`) |
| `IEventPublisher`, `IEventHandler<TEvent>` | contrato de eventos | Spec 002 (`Guara.Core`) |
| `IGuaraBuilder` | raiz da API fluente | Spec 009 (`Guara.Hosting`) |
| `IJobContext`, `JobDelegate`, `IJobMiddleware` | pipeline | Spec 002 (`Guara.Core`) — impl; forma aqui |

**Value types de propriedade deste pacote** (definidos por completo aqui):

- **`JobId`** — identificador opaco de um job. `readonly record struct`; `default(JobId)` representa "nenhum". Representação interna em DD-1.
- **`JobState`** — enum com exatamente: `Created`, `Enqueued`, `Scheduled`, `Processing`, `Succeeded`, `Failed`, `Retrying` (ver [execution-flows](../docs/execution-flows.md)). As **transições** (máquina de estados) são Core (Spec 002).
- **`JobDescriptor`** — descrição imutável e serializável do que executar: tipo-alvo, método, argumentos e metadados/headers. Forma em DD-2.
- **`ScheduleDescriptor`** — descrição de **quando**: imediato / delay (`TimeSpan`) / cron (expressão) / recorrente. Cálculo é do `IScheduler` (Spec 005).
- **Eventos** (`IGuaraEvent`, registros imutáveis no passado): `JobCreated`, `JobScheduled`, `WorkerRequested`, `ExecutorStarted`, `JobCompleted`, `JobFailed`, `JobRetryScheduled` *(adição extend-only 2026-07-18, junto com a retentativa persistente — specs 004/008)*.

Ciclo de vida dos tipos: são **imutáveis**; não há create→archive→delete (não há estado persistido neste pacote). O ciclo de vida do **job** (estados) é modelado por `JobState`, mas transicionado fora daqui.

## API Contract

Este é um pacote de biblioteca — não há endpoints HTTP. O "contrato" é a **superfície de API pública .NET**. Formas ilustrativas (assinaturas finais nas specs donas):

```csharp
namespace Guara.Abstractions;

public readonly record struct JobId(string Value)
{
    public bool IsEmpty => string.IsNullOrEmpty(Value);
}

public enum JobState { Created, Enqueued, Scheduled, Processing, Succeeded, Failed, Retrying }

public interface IGuaraEvent { }
public sealed record JobCreated(JobId Id, DateTimeOffset OccurredAt) : IGuaraEvent;
public sealed record JobCompleted(JobId Id, DateTimeOffset OccurredAt) : IGuaraEvent;
// ... JobScheduled, WorkerRequested, ExecutorStarted, JobFailed

public delegate ValueTask JobDelegate(IJobContext context, CancellationToken ct);

public interface IJobMiddleware
{
    ValueTask InvokeAsync(IJobContext context, JobDelegate next, CancellationToken ct);
}

public interface IJobContext
{
    JobId Id { get; }
    JobDescriptor Descriptor { get; }
    JobState State { get; }
    int Attempt { get; }
    IDictionary<string, object?> Items { get; }
}

public interface IEventPublisher
{
    ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken ct) where TEvent : IGuaraEvent;
}

public interface IGuaraBuilder
{
    IServiceCollection Services { get; }
}
```

**Regras de contrato (invariantes do pacote):**
- Todo membro assíncrono retorna `ValueTask`/`ValueTask<T>` e recebe `CancellationToken` como **último** parâmetro.
- Nenhum tipo público contém lógica de negócio — apenas igualdade de valor e validação/guardas triviais.
- Nomes de eventos no passado; interfaces `I{Componente}`; nada de sufixos de tecnologia.

## Authorization

Não há superfície de execução, logo não há autorização em runtime neste pacote. `IJobContext` **poderá** carregar um principal/claims para o `AuthorizationMiddleware`; a semântica de autorização é da Spec 021 (`Guara.Authorization`). Decisão de incluir `ClaimsPrincipal?` em `IJobContext` → DD-5.

## Edge Cases & Failure Modes

- **`default(JobId)`**: precisa ser detectável como inválido/"nenhum" (`IsEmpty`), evitando ids "zero" silenciosos.
- **Anulabilidade**: pacote com `<Nullable>enable</Nullable>`; contratos declaram nulabilidade explícita.
- **Quebra de compatibilidade**: qualquer remoção/alteração de membro público é falha (ver AC-9) — evolução só por adição.
- **Valores default de struct**: value types devem ter default seguro (sem exceção ao comparar/serializar o default).
- **Ordenação/entrega de eventos**: **não** é responsabilidade deste pacote (é de Core/Hosting).

## Non-Functional Requirements

- **Zero dependências** fora do BCL (nem `Guara.*`, nem terceiros).
- **AOT/Trimming-safe**: compila e publica com `PublishAot=true` sem warnings originados aqui ([ADR-0008](../docs/adr/0008-native-aot-e-trimming.md)).
- **Baixa alocação**: value types como `readonly record struct`; sem boxing nas APIs.
- **Estabilidade**: política **extend-only / semver estrito** verificada por teste de API pública (ex.: `Verify` / Public API Analyzers).
- Volume/latência: N/A (sem comportamento de runtime).

## Integrations

Nenhuma integração externa. Este pacote **define os eventos** que são os pontos de integração *entre componentes* (`JobCreated → … → JobCompleted`), mas não os publica nem os consome.

## Acceptance Criteria

- **AC-1 — Sem dependências.** *Dado* um build de `Guara.Abstractions`, *quando* suas referências são inspecionadas, *então* só há assemblies do BCL (nenhum `Guara.*`, nenhum pacote de terceiros).
- **AC-2 — Sem implementação.** *Dado* qualquer tipo público, *então* ele é interface, delegate, enum, record de evento ou value type imutável — sem lógica de negócio.
- **AC-3 — Estilo assíncrono.** *Dado* qualquer membro assíncrono público, *então* retorna `ValueTask`/`ValueTask<T>` e tem `CancellationToken` como último parâmetro.
- **AC-4 — AOT/Trim limpo.** *Dado* `dotnet publish -p:PublishAot=true` de um consumidor mínimo, *então* nenhum warning de trim/AOT tem origem em `Guara.Abstractions`.
- **AC-5 — Sem extensão de DI.** *Dado* o pacote, *então* não há tipo em `Microsoft.Extensions.DependencyInjection` nem método `AddGuara*`.
- **AC-6 — Eventos.** *Dado* o catálogo de eventos, *então* todos são records imutáveis, nomeados no passado, implementando `IGuaraEvent`.
- **AC-7 — Estados.** *Dado* `JobState`, *então* contém exatamente `{Created, Enqueued, Scheduled, Processing, Succeeded, Failed, Retrying}`.
- **AC-8 — Id vazio.** *Dado* `default(JobId)`, *então* `IsEmpty == true` e o id é tratado como "nenhum".
- **AC-9 — Extend-only.** *Dado* um snapshot da API pública, *quando* um membro é removido/alterado, *então* o teste de compatibilidade falha.
- **AC-10 — Pipeline.** *Dado* `IJobMiddleware`/`JobDelegate`, *então* um middleware consegue curto-circuitar (não chamar `next`) e o tipo que trafega é `IJobContext`.

## Deferred Decisions

- **DD-1 — Representação de `JobId`.** *Fallback escolhido:* `readonly record struct JobId(string Value)` (agnóstico a provider). *Revisão:* Spec 004 (`Guara.Storage`), ao decidir a chave de persistência.
- **DD-2 — Forma de `JobDescriptor`.** *Resolvido:* `sealed record` com argumentos **já serializados** (`ReadOnlyMemory<byte>`) + dicionário de metadados; quem os escreve é o código emitido a partir de `[GuaraJob]`, que conhece os tipos em compilação ([ADR-0019](../docs/adr/0019-guara-serialization-sai-do-catalogo.md)).
- **DD-3 — Logging/métricas.** *Fallback:* reutilizar `Microsoft.Extensions.Logging` + `System.Diagnostics.Metrics`; Abstractions **não** define `ILogger`/`IMetrics` próprios (apesar de citados no `Filosofia.md`). *Revisão:* Spec 016 (`Guara.Diagnostics`).
- **DD-4 — Local dos contratos de Storage.** *Resolvido:* ficam em `Guara.Storage` (pacote de contratos), conforme `Filosofia.md` — **não** em Abstractions. *Ação:* corrigir menção divergente em `components.md`.
- **DD-5 — Principal/claims em `IJobContext`.** *Fallback:* incluir `ClaimsPrincipal? User { get; }` opcional em `IJobContext`. *Revisão:* Spec 021 (`Guara.Authorization`).

## Open Questions

_(vazio — pré-requisito para aprovação; itens em aberto foram convertidos em Deferred Decisions com fallback)_
