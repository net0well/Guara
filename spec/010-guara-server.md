# Spec 010: `Guara.Server` — Lifecycle e Heartbeat

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Server`
**Depende de:** [Spec 001](001-guara-abstractions.md), [Spec 002](002-guara-core.md), [Spec 004](004-guara-storage.md)
**Docs de referência:** [components](../docs/components.md) · [execution-flows](../docs/execution-flows.md)

## Problem

Iniciar e parar, de forma ordenada, os motores (Scheduler, Dispatcher, Worker, Executor), manter o **heartbeat** do nó, e rodar tarefas de **manutenção** (retenção/purga, recuperação de leases expirados). É o "processo servidor" do Guará — o análogo ao `BackgroundJobServer` do Hangfire.

## Scope

### In

- **`IGuaraServer`**: `StartAsync`/`StopAsync` orquestrando o ciclo dos motores.
- **Heartbeat**: registra liveness do nó no storage (para Dashboard e Cluster).
- **Manutenção**: purga por retenção (Spec 004 DD-3), varredura de leases expirados.
- **Shutdown gracioso**: drena o Worker, para o Dispatcher, encerra em ordem.

### Out

- Cálculo de agendamento (Scheduler), busca (Dispatcher), capacidade (Worker), execução (Executor) — apenas os **coordena**.
- Eleição de líder e failover → `Guara.Cluster` (Spec 025); o Server apenas respeita o papel de líder.

## Domain Model

- **`IGuaraServer`** — inicia Dispatcher/Worker/Scheduler-recurring e os loops de heartbeat/manutenção; para em ordem inversa.
- **`ServerOptions`** — `HeartbeatInterval`, `MaintenanceInterval`, `Retention` (Succeeded/Failed), `ShutdownTimeout`.
- **`ServerNode`** — identidade do nó (id, host, iniciado em, último heartbeat) persistida via storage.

## API Contract

```csharp
namespace Guara.Server;

public interface IGuaraServer
{
    ValueTask StartAsync(CancellationToken ct);
    ValueTask StopAsync(CancellationToken ct);
}

public sealed class ServerOptions
{
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public RetentionPolicy Retention { get; set; } = RetentionPolicy.Default;
}
```

## Authorization

N/A (processo de infra). Segredos/conn strings via `Guara.Configuration`.

## Edge Cases & Failure Modes

- **Shutdown** → para de aceitar (Dispatcher), drena Worker até `ShutdownTimeout`, persiste heartbeat final, encerra.
- **Nó não-líder** → não recomputa recurring nem roda manutenção global (evita duplicação); só processa jobs.
- **Heartbeat falha** (storage caiu) → back-off; se persistir, o nó é considerado morto pelos outros (visibility/lease cobre os jobs).
- **Manutenção concorrente** → protegida por `ILockProvider` (só um nó purga por vez).
- **Relógio** → `TimeProvider` injetado.

## Non-Functional Requirements

- Início/parada determinísticos e ordenados; nenhum job perdido no shutdown normal.
- Loops de heartbeat/manutenção de baixo custo; `ValueTask`, `Channel`/timer, sem busy-wait.
- Thread-safe; AOT-safe.

## Integrations

Coordena os motores via seus contratos; persiste heartbeat e roda manutenção via `IJobStorage`/`ILockProvider` (Spec 004); respeita liderança de `Guara.Cluster` (Spec 025).

## Acceptance Criteria

- **AC-1 — Inicia em ordem.** *Dado* `StartAsync`, *então* Executor/Worker/Dispatcher/Scheduler são iniciados e ficam prontos a processar.
- **AC-2 — Heartbeat.** *Dado* o servidor rodando, *então* o nó registra heartbeat a cada `HeartbeatInterval` e aparece como vivo.
- **AC-3 — Shutdown gracioso.** *Dado* `StopAsync`, *então* o Worker drena, o Dispatcher para, e o encerramento respeita `ShutdownTimeout`.
- **AC-4 — Manutenção única.** *Dado* cluster com N nós, *então* a purga por retenção roda em apenas um nó por ciclo.
- **AC-5 — Retenção.** *Dado* jobs além da política de retenção, *quando* a manutenção roda, *então* eles são purgados.
- **AC-6 — Recuperação de lease.** *Dado* um job com lease expirado (nó morto), *então* ele volta a ser elegível.
- **AC-7 — Papel de líder.** *Dado* um nó não-líder, *então* ele não recomputa recurring nem roda manutenção global.

## Deferred Decisions

- **DD-1 — Intervalo de heartbeat.** *Fallback:* 15s. *Revisão:* Spec 025 (Cluster).
- **DD-2 — Retenção default.** *Fallback:* Succeeded 24h / Failed 7d (herda Spec 004 DD-3). *Revisão:* feedback de produção.
- **DD-3 — Servidor no mesmo processo do app vs standalone.** *Fallback:* mesmo processo (via `IHostedService`); modo standalone/worker-service documentado como opção. *Revisão:* pós-MVP.

> **Implementação (2026-07-18):** `GuaraServer` entregue (identidade do nó `maquina:pid:sufixo`, com filas e concorrência visíveis). **Heartbeat com reanúncio**: `HeartbeatAsync` devolvendo `false` (registro removido pela manutenção durante indisponibilidade) faz o nó se reanunciar — espelho do comportamento watchdog/`BackgroundServerGoneException` do Hangfire; falha de storage não derruba o laço. **Manutenção sob lock** (`guara:maintenance`, TTL = um ciclo): entre N nós só um executa por vez (substitui o gating de líder até a Spec 025) — remove nós expirados (`ServerTimeout`, novo, default 1 min) e purga terminais pela retenção. **Recuperação de leases é implícita** na aquisição (jobs `Processing` com lease vencido voltam a ser elegíveis) — não é tarefa de manutenção. `ShutdownTimeout` saiu das opções: o drain pertence ao Worker (`ShutdownDrainTimeout`); o `StopAsync` ordena dispatcher→worker→laços→desregistro (best-effort). `AddGuaraServer()` compõe scheduler+executor+worker+dispatcher (customize um motor chamando o `AddGuara*` dele **antes** — primeira configuração vence) e registra o hosted service idempotentemente.

> **Implementação (2026-07-18) — laço de recorrentes:** terceiro laço do servidor entregue (`RecurringLoopAsync`), no mesmo padrão do de manutenção: poll por `ServerOptions.RecurringPollInterval` (novo, default 15s) sob lock distribuído `guara:recurring` com TTL de um ciclo — entre N nós só um promove por vez. Cada ciclo consulta `Recurring.ListDueAsync(now)` e, por definição vencida: com `SkipIfPreviousRunning` e o job de `LastRunJobId` ainda não-terminal, registra `LastSkippedAt` e reagenda; caso contrário enfileira a ocorrência via `IGuaraClient` (descriptor da definição com a fila dela e `guara-recorrente: <id>` no metadata) e grava `LastRunAt`/`LastRunJobId`/`NextRunAt` recomputado a partir de agora (misfire = uma compensação; sobreposição por padrão). Definição que fica sem próxima ocorrência gera aviso no log. Falha em um ciclo não derruba o laço.

> **Implementação (2026-07-18) — varredura de continuações na manutenção:** o ciclo de manutenção (já sob o lock `guara:maintenance`) ganhou, **antes** das purgas, a chamada a `ContinuationPromoter.SweepAsync` (spec 030): resolve vínculos pendentes cujo pai já finalizou sem promover (queda entre persistir o final e o evento) ou cujo pai não existe mais — a ordem garante o disparo antes de a retenção poder purgar o pai.

## Open Questions

_(vazio)_
