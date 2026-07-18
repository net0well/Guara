# Modelo de Plugins — Referência (Quartz.NET) para o Guará

> **Documento de referência de implementação.** Trechos extraídos do repositório original **Quartz.NET** (Apache-2.0), citados por arquivo como guia de comportamento. **Hangfire não tem um modelo de plugin** de primeira classe (usa filtros/atributos + `IBackgroundProcess`); isto é discutido abaixo. Reimplementar com a arquitetura do Guará (componentes, eventos, middlewares, hosted services).

---

## Panorama

Um **plugin** é uma extensão de ciclo de vida acoplada ao scheduler: recebe callbacks de **inicialização**, **start** e **shutdown** e, tipicamente, se registra como **listener** (de job/trigger/scheduler) para reagir a eventos. É o mecanismo do Quartz para adicionar comportamento transversal (histórico/auditoria, auto-interrupção, carregamento de agendamentos de arquivo, hooks de encerramento) **sem** tocar no núcleo.

| Ferramenta | Modelo de plugin? | Extensibilidade equivalente |
|---|---|---|
| **Quartz.NET** | **Sim** — `ISchedulerPlugin` + listeners | Plugins, `IJobListener`/`ITriggerListener`/`ISchedulerListener`, `IJobFactory` |
| **Hangfire** | Não (sem `IPlugin`) | Filtros (`IServerFilter`/`IClientFilter`/`IElectStateFilter`/`IApplyStateFilter`), `IBackgroundProcess`, `GlobalJobFilters`, extensões de storage |
| **Guará** | Ainda não | Já temos: event bus (`IEventHandler<T>`), pipeline de middlewares (`IJobMiddleware`), `IHostedService`. Um `IGuaraPlugin` seria um **ponto de composição** desses três (proposta abaixo) |

---

## Quartz.NET

### Contrato: `ISchedulerPlugin`

Três métodos de ciclo de vida. `Initialize` roda **durante a criação** do scheduler (antes de o `IJobStore` estar totalmente pronto); `Start` quando o scheduler inicia; `Shutdown` no encerramento.

`src/Quartz/SPI/ISchedulerPlugin.cs`:

```csharp
public interface ISchedulerPlugin
{
    // Chamado na criação do scheduler; recebe o nome e a instância do scheduler.
    ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default);

    // Chamado quando o scheduler é iniciado (já pode fazer chamadas ao scheduler).
    ValueTask Start(CancellationToken cancellationToken = default);

    // Chamado no shutdown para liberar recursos.
    ValueTask Shutdown(CancellationToken cancellationToken = default);
}
```

Detalhes de comportamento:
- Um plugin frequentemente **também implementa um listener** e se auto-registra no `Initialize` via `scheduler.ListenerManager.AddTriggerListener(this)` / `AddJobListener` / `AddSchedulerListener`.
- Pode se publicar no `scheduler.Context[chave] = this` (dicionário `SchedulerContext`) para ser recuperado depois de dentro da execução de um job.
- Configuração fluente por `UsePlugin<T>("nome")` + propriedades no formato `quartz.plugin.<nome>.<prop>` (via `PropertiesSetter`).

### Registro / configuração

`src/Quartz.Plugins/PluginConfigurationExtensions.cs` expõe extensões fluentes que apenas chamam `UsePlugin<T>("nome")` e passam opções:

```csharp
public static T UseJobAutoInterrupt<T>(this T configurer, Action<JobAutoInterruptOptions>? configure = null)
    where T : IPropertyConfigurationRoot
{
    configurer.UsePlugin<JobInterruptMonitorPlugin>("jobAutoInterrupt");
    configure?.Invoke(new JobAutoInterruptOptions(configurer));
    return configurer;
}

// As opções gravam propriedades string (config baseada em chave/valor):
public class JobAutoInterruptOptions : PropertiesSetter
{
    public JobAutoInterruptOptions(IPropertySetter parent) : base(parent, "quartz.plugin.jobAutoInterrupt") { }
    public TimeSpan DefaultMaxRunTime
    {
        set => SetProperty("defaultMaxRunTime", value.TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
    }
}
```

### Catálogo de plugins embutidos

| Plugin | Arquivo | Faz o quê |
|---|---|---|
| `JobInterruptMonitorPlugin` | `Quartz.Plugins/Plugin/Interrupt/` | Interrompe jobs que rodam além de um tempo máximo (via `IScheduler.Interrupt`). É um `ITriggerListener` |
| `LoggingJobHistoryPlugin` / `LoggingTriggerHistoryPlugin` | `Quartz.Plugins/Plugin/History/` | Loga início/fim/veto/misfire de jobs e triggers (auditoria) via listeners; templates de mensagem configuráveis |
| `StructuredLoggingJobHistoryPlugin` / `...TriggerHistoryPlugin` | `Quartz.Plugins/Plugin/History/` | Variante com **logging estruturado** (parâmetros nomeados) |
| `XMLSchedulingDataProcessorPlugin` | `Quartz.Plugins/Plugin/Xml/` | Carrega jobs/triggers de arquivo XML; `scanInterval` permite **hot-reload** |
| `JsonSchedulingDataProcessorPlugin` | `Quartz.Plugins/Plugin/Json/` | Idem, a partir de JSON |
| `ShutdownHookPlugin` | `Quartz.Plugins/Plugin/Management/` | Registra hook de saída do processo para dar shutdown gracioso |

### Exemplo completo: auto-interrupção de jobs longos

`src/Quartz.Plugins/Plugin/Interrupt/JobInterruptMonitorPlugin.cs` — plugin que é também `TriggerListener`:

```csharp
public class JobInterruptMonitorPlugin : TriggerListenerSupport, ISchedulerPlugin
{
    public ValueTask Initialize(string name, IScheduler scheduler, CancellationToken ct = default)
    {
        this.name = name;
        taskScheduler = new QueuedTaskScheduler(1, "JobInterruptMonitorPlugin");
        scheduler.Context[JobInterruptMonitorKey] = this;   // publica-se no SchedulerContext
        this.scheduler = scheduler;
        this.scheduler.ListenerManager.AddTriggerListener(this);  // auto-registro como listener
        return default;
    }

    public override ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken ct = default)
    {
        // Se o job opta por AutoInterruptable, agenda um monitor com atraso = MaxRunTime.
        if (context.JobDetail.JobDataMap.TryGetBoolean(JobDataMapKeyAutoInterruptable, out var v) && v)
        {
            var delay = /* MaxRunTime do JobDataMap ou default 5 min */;
            ScheduleJobInterruptMonitor(context.FireInstanceId, context.JobDetail.Key, delay);
        }
        return default;
    }

    public override ValueTask TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction code, CancellationToken ct = default)
    {
        // Cancela o monitor se o job terminar antes do limite.
        if (interruptMonitors.TryRemove(context.FireInstanceId, out var monitor)) monitor.Cancel();
        return default;
    }

    // O monitor: Task.Delay(delay) e, se não cancelado, scheduler.Interrupt(jobKey).
}
```

### Fluxo passo a passo (Quartz)

1. Na criação do scheduler, cada plugin recebe `Initialize(nome, scheduler)`; ele se registra como listener e/ou se publica no `SchedulerContext`.
2. Ao `scheduler.Start()`, cada plugin recebe `Start()`.
3. Durante a operação, os callbacks de listener (`TriggerFired`, `JobToBeExecuted`, `JobWasExecuted`, `SchedulerShuttingDown`, ...) disparam o comportamento do plugin.
4. No `scheduler.Shutdown()`, cada plugin recebe `Shutdown()` para liberar recursos.

---

## Hangfire (não tem plugins)

O Hangfire **não** expõe uma interface de plugin. A extensibilidade transversal se dá por:
- **Filtros** (atributos): `IServerFilter` (envolve a execução no servidor — ex.: `DisableConcurrentExecutionAttribute`), `IClientFilter` (envolve o enfileiramento), `IElectStateFilter`/`IApplyStateFilter` (interceptam mudanças de estado — ex.: `AutomaticRetryAttribute`). Registrados por atributo no método ou globalmente em `GlobalJobFilters.Filters`.
- **`IBackgroundProcess` / `IBackgroundProcessAsync`**: processos de fundo adicionais rodados pelo `BackgroundJobServer` (ex.: agregadores, watchdogs). É o mais próximo de um "plugin de servidor".
- **Extensões de storage** e de dashboard (rotas/métricas).

---

## Comparação lado a lado

| Aspecto | Quartz (plugin) | Hangfire (filtro/processo) | Guará (proposto) |
|---|---|---|---|
| Ciclo de vida | `Initialize/Start/Shutdown` | Filtros são stateless por invocação; `IBackgroundProcess` tem loop próprio | `IGuaraPlugin.Initialize/Start/Stop` (opcional) |
| Reagir a eventos | Listeners (job/trigger/scheduler) | Filtros de estado/execução | `IEventHandler<TEvent>` (event bus) |
| Interceptar execução | Listeners + `IJob` | `IServerFilter` (`OnPerforming/OnPerformed`) | Middleware `IJobMiddleware` no slot `Custom` |
| Trabalho de fundo | Plugin com timer próprio | `IBackgroundProcess` | `IHostedService` |
| Configuração | `UsePlugin<T>("nome")` + propriedades string | Atributo/`GlobalJobFilters` | `AddGuara...().AddGuaraPlugin<T>()` |

---

## O que o Guará já faz / deve adotar / pode melhorar

O Guará **já tem os três mecanismos** que os plugins do Quartz combinam:
- **Eventos** (`IEventHandler<TEvent>`) = os listeners do Quartz. Auditoria/histórico já é coberta pelo `StateHistory` (semântica) e por handlers de `JobCompleted`/`JobFailed`.
- **Middlewares** (`IJobMiddleware`, slot `Custom`) = interceptar execução.
- **Hosted services** = trabalho de fundo periódico (o `Guara.Server` já é um).

Mapeamento dos plugins do Quartz para o Guará:
- `JobInterruptMonitorPlugin` → já temos o atributo **`[GuaraTempoLimite]`** (cancelamento cooperativo por tempo máximo). Melhoria: o Quartz interrompe via `Interrupt(jobKey)`; nós usamos `CancellationToken` — mais idiomático e cooperativo.
- `LoggingJob/TriggerHistoryPlugin` → nosso **logging estruturado** + `StateHistory` já cobrem; um handler de eventos opcional formaliza a "auditoria".
- `Xml/JsonSchedulingDataProcessorPlugin` → equivalente seria **carregar recorrentes de configuração** (`appsettings`/arquivo) no bootstrap — candidato a um plugin `Guara.Plugins.FileScheduling`.
- `ShutdownHookPlugin` → já resolvido pelo `IHostedService`/lifetime do .NET.

**Proposta de spec (039 — Modelo de Plugins, futuro):** um `IGuaraPlugin` **fino** como ponto de composição opcional:

```csharp
public interface IGuaraPlugin
{
    ValueTask InitializeAsync(IGuaraPluginContext contexto, CancellationToken ct); // registra handlers/middlewares
    ValueTask StartAsync(CancellationToken ct);
    ValueTask StopAsync(CancellationToken ct);
}
// registro: builder.AddGuaraPlugin<MeuPlugin>();
```

Onde `IGuaraPluginContext` dá acesso ao `IServiceCollection`/event bus/pipeline para o plugin "se plugar". Plugins candidatos: `FileScheduling` (recorrentes de arquivo), `AuditHistory` (histórico rico), `DeadLetter` (jobs que esgotaram retry), `Slack/WebhookNotifier`. **Recomendação:** manter opcional e à parte (não no núcleo, ADR-0009); a maior parte já é alcançável hoje sem um "plugin" formal — o `IGuaraPlugin` só agrega ergonomia de empacotamento.

---

## Armadilhas e detalhes sutis

- **`Initialize` roda cedo demais**: no Quartz o `IJobStore` ainda não está pronto no `Initialize` — plugins não devem tocar em dados persistidos ali; use `Start`. No Guará, a fase de `InitializeAsync` deve apenas **registrar** (handlers/middlewares/opções), nunca executar I/O de storage.
- **Ordem de registro** importa (listeners disparam na ordem registrada). Definir ordem determinística.
- **Config string-baseada** do Quartz (`quartz.plugin.x.y`) é frágil; o Guará deve usar **Options tipado** (spec 018) — plugins recebem `IOptions<TPluginOptions>`.
- **Auto-registro em listeners globais** pode gerar overhead por job — plugins de histórico devem ser opt-in e de baixo custo (não logar payload por padrão).
- **Shutdown**: plugins com timers/tasks próprios (como o monitor de interrupção) precisam cancelar e drenar no `Stop` — senão vazam tasks no encerramento.
