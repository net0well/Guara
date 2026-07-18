# Spec 039: Modelo de Plugins

**Status:** Approved (2026-07-18) — recurso **opt-in, pós-1.0**
**Date:** 2026-07-18
**Escopo:** feature — contrato em `Guara.Abstractions`; plugins concretos em pacotes `Guara.Plugins.*` (nunca no núcleo)
**Licença:** OSS (core); plugins podem ser OSS ou comerciais
**Depende de:** [Spec 002](002-guara-core.md) (event bus + middlewares), [Spec 009](009-guara-hosting.md) (hosting), [Spec 010](010-guara-server.md)
**Referência:** [docs/reference/plugins.md](../docs/reference/plugins.md) (análise do modelo do Quartz)

## Problem

Comportamentos transversais — auditoria/histórico rico, carregamento de recorrentes de arquivo/config, dead-letter, notificação por webhook — hoje exigem que o usuário registre à mão um punhado de peças (handlers de evento + middleware + hosted service + options). Falta um **ponto de composição nomeado e instalável** que empacote isso numa unidade só, como o `ISchedulerPlugin` do Quartz — mas construído sobre os mecanismos que o Guará já tem, sem inventar um subsistema novo.

O Quartz resolve com `ISchedulerPlugin` (ciclo de vida `Initialize/Start/Shutdown`) e plugins que se auto-registram como listeners. O Guará não precisa de listeners (temos event bus + middlewares); o plugin é apenas **açúcar de empacotamento** sobre eles.

## Scope

### In

- **`IGuaraPlugin`** — contrato com ciclo de vida: `InitializeAsync` (registrar — sem I/O de storage), `StartAsync`, `StopAsync`.
- **`IGuaraPluginContext`** — passado no `InitializeAsync`; expõe pontos de plug: registrar `IEventHandler<T>`, adicionar `IJobMiddleware` (slot `Custom`), registrar serviços/hosted services, e ler `IConfiguration`.
- **`AddGuaraPlugin<T>()`** — extensão única para instalar um plugin (ordem determinística de registro).
- Plugins candidatos (pacotes à parte): `Guara.Plugins.FileScheduling` (recorrentes de arquivo/config, com scan/hot-reload como o XML/JSON plugin do Quartz), `Guara.Plugins.AuditHistory` (histórico rico além do `StateHistory`), `Guara.Plugins.DeadLetter` (captura jobs que esgotaram retry), `Guara.Plugins.WebhookNotifier`.

### Out

- **Não é do núcleo** (o núcleo nunca referencia plugins nem terceiros). Não é necessário para o 1.0.
- Não substitui event bus/middlewares/hosted services — apenas os empacota.
- Configuração string-baseada estilo `quartz.plugin.x.y` — usamos **Options tipado**.

## Domain Model

- **`IGuaraPlugin`** — ciclo de vida em três fases; `InitializeAsync` roda no bootstrap (apenas registra); `StartAsync`/`StopAsync` acompanham o lifecycle do `Guara.Server`.
- **`IGuaraPluginContext`** — fachada de registro: `Services` (`IServiceCollection`), `AdicionarHandler<TEvent,THandler>()`, `AdicionarMiddleware<TMiddleware>()`, `Configuration`.
- **Ordem**: plugins iniciam na ordem de registro; handlers/middlewares que contribuem entram nos slots canônicos existentes.
- Um plugin com trabalho de fundo próprio registra um `IHostedService` (não roda timers soltos).

## API Contract

```csharp
namespace Guara.Abstractions;

public interface IGuaraPlugin
{
    // Bootstrap: apenas registra handlers/middlewares/serviços. Sem I/O de storage aqui.
    ValueTask InitializeAsync(IGuaraPluginContext contexto, CancellationToken ct);
    ValueTask StartAsync(CancellationToken ct);
    ValueTask StopAsync(CancellationToken ct);
}

public interface IGuaraPluginContext
{
    IServiceCollection Services { get; }
    IConfiguration Configuration { get; }
    IGuaraPluginContext AdicionarHandler<TEvent, THandler>()
        where TEvent : IGuaraEvent where THandler : class, IEventHandler<TEvent>;
    IGuaraPluginContext AdicionarMiddleware<TMiddleware>() where TMiddleware : class, IJobMiddleware;
}

// registro:
builder.AddGuara().AddGuaraPlugin<AuditHistoryPlugin>();
```

## Authorization

N/A no contrato. Plugins que exponham rotas no dashboard respeitam as permissões da [Spec 021](021-guara-authorization.md).

## Edge Cases & Failure Modes

- **`InitializeAsync` tocando storage**: proibido (storage pode não estar pronto) — apenas registrar; trabalho de I/O vai em `StartAsync`/hosted service.
- **Plugin que lança no `InitializeAsync`**: falha o startup com o nome do plugin (fail-fast, não "meio-inicializado").
- **Ordem entre plugins**: registro determinístico; documentado que middlewares/handlers de plugins entram após os do núcleo no mesmo slot.
- **`StopAsync`**: plugins com tasks/timers próprios devem cancelar e drenar (senão vazam no shutdown).
- **Plugin comercial sem licença**: não ativa; erro claro; núcleo OSS segue funcionando.

## Non-Functional Requirements

- Contrato em `Guara.Abstractions` (sem dependências novas). Plugins isolados em pacotes próprios.
- Zero reflection no runtime do plugin (descoberta/registro explícitos); AOT-safe.
- Baixo overhead: handlers/middlewares de plugin só custam quando registrados; auditoria não loga payload por padrão.

## Integrations

Constrói sobre o event bus e o pipeline (Spec 002), o hosting (Spec 009) e o lifecycle do servidor (Spec 010). Plugins de agendamento por arquivo usam `IGuaraClient`/recorrentes (Spec 005/038).

## Acceptance Criteria

- **AC-1 — Ciclo de vida.** *Dado* um `IGuaraPlugin` registrado, *então* recebe `InitializeAsync` no bootstrap e `StartAsync`/`StopAsync` no lifecycle do servidor.
- **AC-2 — Composição.** *Dado* um plugin que adiciona um `IEventHandler<JobFailed>` e um middleware, *então* ambos passam a operar sem o usuário registrá-los manualmente.
- **AC-3 — Isolamento do núcleo.** *Dado* o grafo de dependências, *então* nenhum pacote de núcleo referencia `Guara.Plugins.*`.
- **AC-4 — Fail-fast.** *Dado* um plugin que lança no `InitializeAsync`, *então* o startup falha citando o plugin.
- **AC-5 — Shutdown limpo.** *Dado* um plugin com trabalho de fundo, *quando* o servidor para, *então* o plugin drena/cancela sem vazar tasks.
- **AC-6 — AOT.** *Dado* `PublishAot=true`, *então* o registro do plugin funciona sem warnings.

## Deferred Decisions

- **DD-1 — Descoberta automática por assembly.** *Fallback:* registro explícito `AddGuaraPlugin<T>()`; varredura por atributo `[assembly: GuaraPlugins]` via source generator é possível depois (alinhado à Spec 029). *Revisão:* pós-1.0.
- **DD-2 — Conjunto inicial de plugins oficiais.** *Fallback:* `FileScheduling` e `AuditHistory` primeiro; demais sob demanda. *Revisão:* feedback.
- **DD-3 — Prioridade/ordenação explícita entre plugins.** *Fallback:* ordem de registro; atributo de ordem se necessário. *Revisão:* pós-1.0.

## Open Questions

_(vazio)_
