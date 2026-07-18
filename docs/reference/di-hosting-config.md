# DI, Hosting e Configuração — Referência (Hangfire x Quartz.NET) para o Guará

> **Nota de abertura.** Este é um documento de **referência de implementação**. Os trechos de código são retirados dos repositórios originais — **Hangfire** (licença LGPL v3) e **Quartz.NET** (licença Apache-2.0) — e citados sempre com o caminho do arquivo de origem. Servem como **guia de comportamento**, para entendermos *como* cada projeto resolve o problema e decidirmos o que o Guará adota, adapta ou evita. **Não é para copiar literalmente** — o Guará tem invariantes próprias (zero reflection/AOT-safe, sem singleton estático, API do usuário em português, um `AddGuara...()` por pacote, padrão Options com validação antecipada). Onde uma ferramenta *não tem* a funcionalidade, isso é dito explicitamente.

---

## Panorama

As três bibliotecas resolvem o mesmo trio de problemas de bootstrap em uma aplicação .NET moderna:

1. **DI** — como registrar os serviços do framework no `IServiceCollection`.
2. **Hosting** — como amarrar o ciclo de vida do "servidor de jobs" ao Generic Host via `IHostedService`.
3. **Configuração** — como o usuário descreve *o quê* rodar e *como* (storage, concorrência, jobs/triggers, filtros), com validação.

As filosofias divergem profundamente:

- **Hangfire** tem duas fases distintas: `AddHangfire(...)` (configuração global, fluente, apoiada em **estado estático**: `GlobalConfiguration.Configuration`, `JobStorage.Current`, `JobActivator.Current`, `GlobalJobFilters.Filters`) e `AddHangfireServer(...)` (registra o `IHostedService` que dá vida ao `BackgroundJobServer`). A configuração é **fluente e stateful** — cada `Use...()` produz um efeito colateral em um singleton estático e devolve um wrapper tipado `IGlobalConfiguration<T>` para encadear ajustes contextuais.

- **Quartz.NET** adota o **padrão Options canônico do .NET**. Tudo converge para um `QuartzOptions` (que é literalmente um `Dictionary<string,string?>` de chaves `quartz.*` mais listas de jobs/triggers). `AddQuartz(...)` popula esse dicionário via um `SchedulerBuilder` fluente e registra `IConfigureOptions<QuartzOptions>` para jobs/triggers/listeners; `AddQuartzHostedService()` liga o `IHostedService`. Suporta **binding nativo de `appsettings.json`**, **múltiplos schedulers nomeados** e **configuração diferida com acesso ao `IServiceProvider`**.

- **Guará** (specs 009/010/018) escolhe o caminho do Quartz em espírito (padrão Options, sem estado estático) mas com a ergonomia composicional do Hangfire (`AddGuara().Use...Storage().AddGuaraServer()`), acrescentando o que **nenhum dos dois** faz por padrão: **zero reflection / AOT-safe** (wiring por source generator, spec 029), **validação antecipada obrigatória** (`IValidateOptions` + `ValidateOnStart`, spec 018) e **API de usuário em português**.

Um ponto estrutural: no Guará **cada pacote expõe exatamente uma extensão** (`AddGuara...()`/`Use...()`) no namespace `Microsoft.Extensions.DependencyInjection` (ADR-0006). Hangfire concentra tudo em `HangfireServiceCollectionExtensions` (+ `GlobalConfigurationExtensions` com dezenas de `Use...`); Quartz concentra em `ServiceCollectionExtensions`/`QuartzServiceCollectionExtensions`.

---

## Hangfire

### Visão geral

Hangfire divide o bootstrap em **duas responsabilidades separadas**:

- **`AddHangfire(Action<IGlobalConfiguration>)`** — registra os serviços de núcleo (storage, activator, client, recurring manager, filtros) e **agenda a execução do callback de configuração**. O callback **não roda na hora**: roda **preguiçosamente**, uma única vez, quando o singleton `IGlobalConfiguration` é resolvido pela primeira vez.
- **`AddHangfireServer(...)`** — registra um `IHostedService` (`BackgroundJobServerHostedService`) que constrói e inicia um `BackgroundJobServer` no boot do host.

A configuração é **globalmente estática**: `UseXStorage()` seta `JobStorage.Current`, `UseActivator()` seta `JobActivator.Current`, `UseFilter()` faz `GlobalJobFilters.Filters.Add(...)`. Os registros de DI são apenas *pontes* que leem esse estado estático.

### Classes-chave

| Arquivo | Responsabilidade |
|---|---|
| `Hangfire.NetCore/HangfireServiceCollectionExtensions.cs` | `AddHangfire` (2 overloads), `AddHangfireServer` (8 overloads), `TryAddSingletonChecked`, `ThrowIfNotConfigured`, `GetInternalServices` |
| `Hangfire.Core/GlobalConfiguration.cs` | Singleton estático `GlobalConfiguration.Configuration`; `CompatibilityLevel` |
| `Hangfire.Core/IGlobalConfiguration.cs` | Interface marcadora vazia + `IGlobalConfiguration<out T>` com `Entry` (para encadeamento tipado) |
| `Hangfire.Core/GlobalConfigurationExtensions.cs` | Todos os `Use...()` fluentes: `UseStorage`, `UseActivator`, `UseFilter`, `UseLogProvider`, `UseSerializerSettings`, `UseResultsInContinuations`, etc.; o primitivo `Use<T>` |
| `Hangfire.Core/JobActivator.cs` | `JobActivator.Current` estático; `ActivateJob`/`BeginScope` (ativação de jobs) |
| `Hangfire.NetCore/AspNetCore/AspNetCoreJobActivator.cs` | Activator que cria um **DI scope por job** via `IServiceScopeFactory` |
| `Hangfire.NetCore/AspNetCore/AspNetCoreJobActivatorScope.cs` | Escopo: `Resolve` via `ActivatorUtilities.GetServiceOrCreateInstance`; dispose async-aware |
| `Hangfire.NetCore/BackgroundJobServerHostedService.cs` | `IHostedService` que constrói/para o `BackgroundJobServer`; integra `IHostApplicationLifetime` |
| `Hangfire.NetCore/BackgroundProcessingServerHostedService.cs` | `IHostedService` para um `IBackgroundProcessingServer` customizado |
| `Hangfire.NetCore/DefaultClientManagerFactory.cs` | Fábrica de `IBackgroundJobClient`/`IRecurringJobManager` por storage |
| `Hangfire.AspNetCore/HangfireApplicationBuilderExtensions.cs` | `UseHangfireDashboard`, `UseHangfireServer` (legado), `RegisterHangfireServer` |
| `Hangfire.AspNetCore/HangfireEndpointRouteBuilderExtensions.cs` | `MapHangfireDashboard` (endpoint routing) |

### Trechos de código

**1) `AddHangfire` — registros com `TryAddSingletonChecked` e o marcador `IGlobalConfiguration`**

`Hangfire.NetCore/HangfireServiceCollectionExtensions.cs`
```csharp
public static IServiceCollection AddHangfire(
    [NotNull] this IServiceCollection services,
    [NotNull] Action<IServiceProvider, IGlobalConfiguration> configuration)
{
    if (services == null) throw new ArgumentNullException(nameof(services));
    if (configuration == null) throw new ArgumentNullException(nameof(configuration));

    services.TryAddSingletonChecked(static _ => JobStorage.Current);
    services.TryAddSingletonChecked(static _ => JobActivator.Current);

    services.TryAddSingleton(static _ => DashboardRoutes.Routes);
    services.TryAddSingleton<IJobFilterProvider>(static _ => JobFilterProviders.Providers);
    services.TryAddSingleton<ITimeZoneResolver>(static _ => new DefaultTimeZoneResolver());
    // ... factories de client/manager (V1 e V2) ...

    // IGlobalConfiguration é o MARCADOR de que o Hangfire foi adicionado
    // (checado por ThrowIfNotConfigured). Sendo singleton, garante que o
    // callback de configuração roda UMA vez. Nunca deve ser substituído;
    // AddSingleton() lança se já registrado.
    services.AddSingleton<IGlobalConfiguration>(serviceProvider =>
    {
        var configurationInstance = GlobalConfiguration.Configuration; // <-- singleton ESTÁTICO

        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        if (loggerFactory != null)
            configurationInstance.UseLogProvider(new AspNetCoreLogProvider(loggerFactory));

        var scopeFactory = serviceProvider.GetService<IServiceScopeFactory>();
        if (scopeFactory != null)
            configurationInstance.UseActivator(new AspNetCoreJobActivator(scopeFactory));

        configuration(serviceProvider, configurationInstance); // callback do usuário
        return configurationInstance;
    });

    return services;
}
```

Detalhe crítico: o **default de log provider e activator é aplicado *antes* do callback do usuário**, portanto o usuário pode sobrescrevê-los. E toda a configuração corre **dentro da factory do singleton** — ou seja, **preguiçosamente**, na primeira resolução, não em `AddHangfire`.

**2) `TryAddSingletonChecked` — força a configuração antes de entregar qualquer serviço**

`Hangfire.NetCore/HangfireServiceCollectionExtensions.cs`
```csharp
private static void TryAddSingletonChecked<T>(
    [NotNull] this IServiceCollection serviceCollection,
    [NotNull] Func<IServiceProvider, T> implementationFactory)
    where T : class
{
    serviceCollection.TryAddSingleton<T>(serviceProvider =>
    {
        if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));
        serviceProvider.GetRequiredService<IGlobalConfiguration>(); // garante que a config rodou
        return implementationFactory(serviceProvider);
    });
}
```

Qualquer resolução de `JobStorage`, `IBackgroundJobClient`, etc. primeiro resolve `IGlobalConfiguration`, disparando o callback uma única vez. É o mecanismo que sincroniza "config estática ↔ DI".

**3) O primitivo fluente `Use<T>` e o encadeamento tipado**

`Hangfire.Core/GlobalConfigurationExtensions.cs`
```csharp
public static IGlobalConfiguration<TStorage> UseStorage<TStorage>(
    [NotNull] this IGlobalConfiguration configuration,
    [NotNull] TStorage storage) where TStorage : JobStorage
{
    if (configuration == null) throw new ArgumentNullException(nameof(configuration));
    if (storage == null) throw new ArgumentNullException(nameof(storage));
    return configuration.Use(storage, static x => JobStorage.Current = x); // efeito colateral ESTÁTICO
}

public static IGlobalConfiguration UseFilter<TFilter>(
    [NotNull] this IGlobalConfiguration configuration, [NotNull] TFilter filter)
{
    // ...
    return configuration.Use(filter, static x => GlobalJobFilters.Filters.Add(x));
}

[EditorBrowsable(EditorBrowsableState.Never)]
public static IGlobalConfiguration<T> Use<T>(
    [NotNull] this IGlobalConfiguration configuration, T entry,
    [NotNull] Action<T> entryAction)
{
    entryAction(entry);                       // aplica o efeito colateral
    return new ConfigurationEntry<T>(entry);  // devolve wrapper com .Entry para o próximo elo
}
```

Assim `UseStorage(store).WithJobExpirationTimeout(ts)` funciona: `UseStorage` devolve `IGlobalConfiguration<TStorage>`, e `WithJobExpirationTimeout` acessa `.Entry.JobExpirationTimeout`.

**4) `AddHangfireServer` — registra o `IHostedService` (como Transient!)**

`Hangfire.NetCore/HangfireServiceCollectionExtensions.cs`
```csharp
private static IServiceCollection AddHangfireServerInner(
    [NotNull] IServiceCollection services,
    [CanBeNull] JobStorage storage,
    [CanBeNull] IEnumerable<IBackgroundProcess> additionalProcesses,
    [NotNull] Action<IServiceProvider, BackgroundJobServerOptions> optionsAction)
{
    services.AddTransient<IHostedService, BackgroundJobServerHostedService>(provider =>
    {
        var options = new BackgroundJobServerOptions();
        optionsAction(provider, options);
        return CreateBackgroundJobServerHostedService(provider, storage, additionalProcesses, options);
    });
    return services;
}

private static BackgroundJobServerHostedService CreateBackgroundJobServerHostedService(
    IServiceProvider provider, JobStorage storage,
    IEnumerable<IBackgroundProcess> additionalProcesses, BackgroundJobServerOptions options)
{
    ThrowIfNotConfigured(provider); // exige AddHangfire prévio

    storage = storage ?? provider.GetService<JobStorage>() ?? JobStorage.Current;
    additionalProcesses = additionalProcesses ?? provider.GetServices<IBackgroundProcess>();

    options.Activator       = options.Activator       ?? provider.GetService<JobActivator>();
    options.FilterProvider  = options.FilterProvider  ?? provider.GetService<IJobFilterProvider>();
    options.TimeZoneResolver = options.TimeZoneResolver ?? provider.GetService<ITimeZoneResolver>();

    GetInternalServices(provider, out var factory, out var stateChanger, out var performer);
    var lifetime = provider.GetService<IHostApplicationLifetime>();
    return new BackgroundJobServerHostedService(
        storage, options, additionalProcesses, factory, performer, stateChanger, lifetime);
}
```

Observações: (a) o `IHostedService` é registrado como **Transient** — incomum, mas cada `AddHangfireServer` empilha um hosted service, e é assim que se sobem **múltiplos servidores** no mesmo processo. (b) `storage` cai em cascata: parâmetro → DI → `JobStorage.Current` estático. (c) opções recebem defaults do container só se ainda nulas.

**5) `BackgroundJobServerHostedService` — start não-bloqueante e defer para `ApplicationStarted`**

`Hangfire.NetCore/BackgroundJobServerHostedService.cs`
```csharp
public Task StartAsync(CancellationToken cancellationToken)
{
    if (_hostApplicationLifetime != null)
    {
        // https://github.com/HangfireIO/Hangfire/issues/2117
        _hostApplicationLifetime.ApplicationStarted.Register(InitializeProcessingServer);
    }
    else
    {
        InitializeProcessingServer();
    }
    return Task.CompletedTask; // não bloqueia o startup
}

public async Task StopAsync(CancellationToken cancellationToken)
{
    var server = _processingServer;
    if (server == null) return;
    try
    {
        server.SendStop();
        await server.WaitForShutdownAsync(cancellationToken);
    }
    catch (ObjectDisposedException)
    {
        // Workaround p/ bug do Testing do ASP.NET Core (StopAsync chamado 2x)
        // https://github.com/dotnet/aspnetcore/issues/40271
    }
}
```

O construtor registra `SendStopSignal` em `ApplicationStopping`, garantindo que o servidor recebe o sinal de parada mesmo antes de `StopAsync`.

**6) Ativação de job com escopo de DI por execução**

`Hangfire.NetCore/AspNetCore/AspNetCoreJobActivatorScope.cs`
```csharp
public override object Resolve(Type type)
{
    return ActivatorUtilities.GetServiceOrCreateInstance(_serviceScope.ServiceProvider, type);
}

public override void DisposeScope()
{
    if (_serviceScope is IAsyncDisposable asyncDisposable)
    {
        asyncDisposable.DisposeAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        return;
    }
    _serviceScope.Dispose();
}
```

`GetServiceOrCreateInstance` resolve o tipo do container **ou** o instancia com injeção de construtor se não registrado — permite jobs não registrados no DI mas com dependências injetáveis.

### Fluxo passo a passo (Hangfire)

1. `builder.Services.AddHangfire(cfg => cfg.UseXStorage(conn).UseFilter(...))` — **nada roda ainda**; registra factories (`TryAddSingletonChecked`) e agenda o callback dentro da factory de `IGlobalConfiguration`.
2. `builder.Services.AddHangfireServer()` — registra `IHostedService` (Transient) apontando para `BackgroundJobServerHostedService`.
3. No boot do host, o DI resolve os `IHostedService`. Ao criar o hosted service, `CreateBackgroundJobServerHostedService` chama `ThrowIfNotConfigured` → resolve `IGlobalConfiguration` → **dispara o callback** (default log/activator + callback do usuário → seta `JobStorage.Current`, filtros, etc.).
4. `StartAsync` registra `InitializeProcessingServer` em `ApplicationStarted` (ou executa já) e retorna imediatamente.
5. Quando a aplicação sinaliza `ApplicationStarted`, o `BackgroundJobServer` é criado e começa a processar.
6. No shutdown: `ApplicationStopping` → `SendStop`; `StopAsync` → `WaitForShutdownAsync`.

---

## Quartz.NET

### Visão geral

Quartz.NET usa **integralmente o padrão Options do .NET**. O objeto central é o `QuartzOptions`, um `Dictionary<string,string?>` com as chaves `quartz.*` do scheduler mais listas internas de `IJobDetail`/`ITrigger`. O fluxo:

- **`AddQuartz(Action<IServiceCollectionQuartzConfigurator>)`** — cria um `SchedulerBuilder` (fluente, escreve propriedades `quartz.*`), roda o callback do usuário (que pode `AddJob`/`AddTrigger`/`ScheduleJob`/`UsePersistentStore`/...), registra serviços de suporte e copia as propriedades para o `QuartzOptions` via `services.Configure<QuartzOptions>(...)`.
- **`AddQuartzHostedService()`** — registra o `IHostedService` (`QuartzHostedService`) que obtém o scheduler da `ISchedulerFactory` e o inicia.
- A `ISchedulerFactory` é a `ServiceCollectionSchedulerFactory`, que **constrói o scheduler preguiçosamente** a partir de `IOptions<QuartzOptions>`, resolve componentes do DI e amarra jobs/triggers/listeners/calendars.

Suporta **binding nativo de `appsettings.json`** (overload `AddQuartz(IConfiguration)`), **schedulers nomeados** (`AddQuartz("nome", ...)`), **múltiplos schedulers** por processo e **configuração diferida com `IServiceProvider`**.

### Classes-chave

| Arquivo | Responsabilidade |
|---|---|
| `Quartz/Configuration/ServiceCollectionExtensions.cs` | `AddQuartz` (7 overloads: `Action`, `IConfiguration`, nomeado, `NameValueCollection`, com `IServiceProvider` diferido), `AddJob`, `AddTrigger`, `ScheduleJob`, `AddCalendar`, `AddDataSourceProvider` |
| `Quartz/Hosting/QuartzServiceCollectionExtensions.cs` | `AddQuartzHostedService` / `AddQuartzHostedService<T>` — registra o(s) `IHostedService` |
| `Quartz/Hosting/QuartzHostedService.cs` | `IHostedLifecycleService`: obtém e inicia/para o scheduler; defer para `ApplicationStarted` |
| `Quartz/Hosting/QuartzHostedServiceOptions.cs` | `WaitForJobsToComplete`, `StartDelay`, `AwaitApplicationStarted` (default `true`) |
| `Quartz/Hosting/NamedSchedulerHostedService.cs` | Sobe/desce todos os schedulers **nomeados**; no-op se nenhum |
| `Quartz/Configuration/QuartzOptions.cs` | `Dictionary<string,string?>` + `_jobDetails`/`_triggers` + listas diferidas; acessores tipados (`SchedulerName`, `MisfireThreshold`, ...) |
| `Quartz/SchedulerBuilder.cs` | Builder fluente das propriedades: `UseInMemoryStore`, `UsePersistentStore`, `UseThreadPool`, `UseJobFactory`, `UseTypeLoader`, `SchedulerId/Name` |
| `Quartz/Configuration/IServiceCollectionQuartzConfigurator.cs` | Contrato do configurador (Services, OptionsName, Use*, Add*Listener, UseExecutionLimits) |
| `Quartz/Configuration/ServiceCollectionQuartzConfigurator.cs` | Implementação; ramifica comportamento default vs nomeado (`IsNamedScheduler`) |
| `Quartz/Configuration/ServiceCollectionSchedulerFactory.cs` | `ISchedulerFactory` que constrói o scheduler de `IOptions<QuartzOptions>`, com `InstantiateType` resolvendo do DI |
| `Quartz/Configuration/QuartzConfiguration.cs` | `IPostConfigureOptions<QuartzOptions>` — **gancho de validação (hoje vazio/no-op)** |
| `Quartz/Configuration/QuartzConfigurationHelper.cs` | Achata `IConfiguration` → `NameValueCollection` (`Scheduler:InstanceName` → `quartz.scheduler.instanceName`) |
| `Quartz.AspNetCore/AspNetCore/QuartzServiceCollectionExtensions.cs` | `AddQuartzHealthChecks`, `AddHttpApi`, `MapQuartzApi` |

### Trechos de código

**1) `AddQuartz` base — registros com `TryAdd*`, defaults e cópia para `QuartzOptions`**

`Quartz/Configuration/ServiceCollectionExtensions.cs`
```csharp
public static IServiceCollection AddQuartz(
    this IServiceCollection services,
    NameValueCollection properties,
    Action<IServiceCollectionQuartzConfigurator>? configure = null)
{
    services.AddOptions();

    var schedulerBuilder = SchedulerBuilder.Create(properties);
    if (configure is not null)
    {
        var target = new ServiceCollectionQuartzConfigurator(services, schedulerBuilder);
        configure(target); // roda AGORA (mas jobs/triggers ficam diferidos como IConfigureOptions)
    }

    services.TryAddSingleton<IDbConnectionManager, DBConnectionManager>();
    services.TryAddSingleton<ISchedulerRepository, SchedulerRepository>();

    if (string.IsNullOrWhiteSpace(properties[StdSchedulerFactory.PropertySchedulerTypeLoadHelperType]))
        services.TryAddSingleton<ITypeLoadHelper, SimpleTypeLoadHelper>();

    services.TryAddSingleton(TimeProvider.System);
    if (string.IsNullOrWhiteSpace(properties[StdSchedulerFactory.PropertySchedulerJobFactoryType]))
    {
        // sem job factory explícita → usa a versão MS (resolve jobs do DI)
        properties[StdSchedulerFactory.PropertySchedulerJobFactoryType] =
            typeof(MicrosoftDependencyInjectionJobFactory).AssemblyQualifiedNameWithoutVersion();
        services.TryAddSingleton<IJobFactory, MicrosoftDependencyInjectionJobFactory>();
    }

    services.Configure<QuartzOptions>(options =>
    {
        foreach (var key in schedulerBuilder.Properties.AllKeys)
            if (key is not null) options[key] = schedulerBuilder.Properties[key]; // copia props p/ Options
    });

    services.TryAddSingleton<ContainerConfigurationProcessor>();
    services.TryAddSingleton<ISchedulerFactory, ServiceCollectionSchedulerFactory>();

    services.TryAddEnumerable([
        ServiceDescriptor.Singleton<IPostConfigureOptions<QuartzOptions>, QuartzConfiguration>()
    ]);
    return services;
}
```

**2) `AddJob`/`AddTrigger`/`ScheduleJob` — jobs/triggers como `IConfigureOptions<QuartzOptions>` diferidos**

`Quartz/Configuration/ServiceCollectionExtensions.cs`
```csharp
public static IServiceCollectionQuartzConfigurator AddJob(
    this IServiceCollectionQuartzConfigurator options,
    Type jobType, JobKey? jobKey = null,
    Action<IServiceProvider, IJobConfigurator>? configure = null)
{
    if (!typeof(IJob).IsAssignableFrom(jobType))
        Throw.ArgumentException("jobType must implement the IJob interface", nameof(jobType));

    var c = JobBuilder.Create();
    if (jobKey is not null) c.WithIdentity(jobKey);

    var optionsName = options.OptionsName; // "" para default; nome p/ scheduler nomeado
    options.Services.AddSingleton<IConfigureOptions<QuartzOptions>>(serviceProvider =>
    {
        var jobDetail = ConfigureAndBuildJobDetail(serviceProvider, jobType, c, configure, out _);
        return new ConfigureNamedOptions<QuartzOptions>(optionsName, x => x._jobDetails.Add(jobDetail));
    });
    return options;
}
```

Em `ScheduleJob<T>`, se o job não recebeu chave própria, a chave do job é **casada com a chave do trigger** e valida que o trigger referencia o job certo:

`Quartz/Configuration/ServiceCollectionExtensions.cs`
```csharp
if (!jobHasCustomKey)
{
    ((JobDetailImpl) jobDetail).Key = new JobKey(t.Key.Name, t.Key.Group);
    ((IMutableTrigger) t).JobKey = jobDetail.Key; // mantém ITrigger.JobKey sincronizado
}
if (t.JobKey is null || !t.JobKey.Equals(jobDetail.Key))
    Throw.InvalidOperationException("Trigger doesn't refer to job being scheduled");
```

**3) Binding de `appsettings.json` (nativo) e schedulers nomeados**

`Quartz/Configuration/ServiceCollectionExtensions.cs`
```csharp
public static IServiceCollection AddQuartz(
    this IServiceCollection services, IConfiguration configuration,
    Action<IServiceCollectionQuartzConfigurator>? configure = null)
{
    var schedulersSection = configuration.GetSection("Schedulers");
    var hasNamedSchedulers = schedulersSection.Exists();
    var hasDirectConfig = HasDirectSchedulerConfiguration(configuration);

    if (hasNamedSchedulers && hasDirectConfig)
        throw new SchedulerConfigException(
            "The Quartz configuration section contains both a 'Schedulers' sub-section and direct scheduler configuration...");

    if (hasNamedSchedulers)
    {
        foreach (var child in schedulersSection.GetChildren())
            AddQuartz(services, child.Key, child, configure); // um scheduler nomeado por filho
        return services;
    }

    var properties = QuartzConfigurationHelper.ToNameValueCollection(configuration);
    AddQuartz(services, properties, configure);
    JsonSchedulingHelper.ConfigureOptionsFromConfiguration(services, configuration); // Schedule/Scheduling
    return services;
}
```

A conversão hierárquica→plana (`QuartzConfigurationHelper`):

`Quartz/Configuration/QuartzConfigurationHelper.cs`
```csharp
private static void FlattenSection(IConfigurationSection section, string currentPath, NameValueCollection properties)
{
    if (section.Value is not null)
        properties["quartz." + currentPath] = section.Value;
    foreach (var child in section.GetChildren())
        FlattenSection(child, currentPath + "." + ToCamelCase(child.Key), properties);
}
// "Scheduler:InstanceName" (JSON) → "quartz.scheduler.instanceName" (property)
```

**4) `AddQuartzHostedService` — registro condicional e ordem obrigatória**

`Quartz/Hosting/QuartzServiceCollectionExtensions.cs`
```csharp
public static IServiceCollection AddQuartzHostedService<[DynamicallyAccessedMembers(...)] T>(
    this IServiceCollection services, Action<QuartzHostedServiceOptions>? configure = null)
    where T : QuartzHostedService
{
    if (configure is not null) services.Configure(configure);

    // Só registra o QuartzHostedService default SE existir uma ISchedulerFactory
    // (i.e. o usuário chamou AddQuartz() sem nome). Não pode ser incondicional:
    // QuartzHostedService exige ISchedulerFactory no construtor e o DI falharia no boot
    // quando só há schedulers nomeados. => AddQuartz() DEVE vir ANTES de AddQuartzHostedService().
    if (services.Any(d => d.ServiceType == typeof(ISchedulerFactory)))
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, T>());

    // NamedSchedulerHostedService sempre registrado; no-op se não houver schedulers nomeados
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, NamedSchedulerHostedService>());
    return services;
}
```

Note: hosted service é **Singleton** (via `TryAddEnumerable`, para não registrar duplicado), oposto ao Transient do Hangfire.

**5) `QuartzHostedService` — defer de start para depois do startup da aplicação**

`Quartz/Hosting/QuartzHostedService.cs`
```csharp
public virtual async Task StartAsync(CancellationToken cancellationToken)
{
    try
    {
        scheduler = await schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);

        if (options.Value.AwaitApplicationStarted) // default: NÃO roda jobs durante o startup
        {
            startupTask = AwaitStartupCompletionAndStartSchedulerAsync(cancellationToken);
            if (startupTask.IsCompleted) await startupTask.ConfigureAwait(false);
        }
        else // legado: inicia inline
        {
            startupTask = StartSchedulerAsync(cancellationToken);
            await startupTask.ConfigureAwait(false);
        }
    }
    catch (OperationCanceledException) { /* cancelado → não inicia */ }
}

private async Task AwaitStartupCompletionAndStartSchedulerAsync(CancellationToken startupCancellationToken)
{
    using var combined = CancellationTokenSource.CreateLinkedTokenSource(
        startupCancellationToken, applicationLifetime.ApplicationStarted);

    await Task.Delay(Timeout.InfiniteTimeSpan, combined.Token)  // espera "infinita" até ApplicationStarted
        .ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.OnlyOnCanceled, TaskScheduler.Default)
        .ConfigureAwait(false);

    if (!startupCancellationToken.IsCancellationRequested)
        await StartSchedulerAsync(applicationLifetime.ApplicationStopping).ConfigureAwait(false);
}
```

E o shutdown respeita `WaitForJobsToComplete`:

`Quartz/Hosting/QuartzHostedService.cs`
```csharp
public virtual async Task StopAsync(CancellationToken cancellationToken)
{
    if (scheduler is null || startupTask is null) return; // parado sem ter iniciado
    try
    {
        await Task.WhenAny(startupTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
    }
    finally
    {
        // sempre chama Shutdown p/ desligar o scheduler do repositório global
        await scheduler.Shutdown(options.Value.WaitForJobsToComplete, cancellationToken).ConfigureAwait(false);
    }
}
```

**6) `ServiceCollectionSchedulerFactory` — construção preguiçosa + resolução via DI**

`Quartz/Configuration/ServiceCollectionSchedulerFactory.cs`
```csharp
private async ValueTask<IScheduler> EnsureSchedulerCreated(CancellationToken cancellationToken)
{
    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false); // init único, thread-safe
    try
    {
        if (initializedScheduler is null)
            Initialize(options.Value.ToNameValueCollection()); // QuartzOptions → propriedades

        var scheduler = await base.GetScheduler(cancellationToken).ConfigureAwait(false);
        if (!ReferenceEquals(scheduler, initializedScheduler))
        {
            await InitializeScheduler(scheduler, cancellationToken).ConfigureAwait(false);
            initializedScheduler = scheduler;
        }
        return scheduler;
    }
    finally { semaphore.Release(); }
}

// componentes (thread pool, job store, listeners...) são resolvidos do DI antes de instanciar por reflection
protected override T InstantiateType<T>(Type? implementationType)
{
    var service = serviceProvider.GetService<T>();
    if (service is not null) return service;
    // ... singletons diferidos ...
    return ObjectUtils.InstantiateType<T>(implementationType);
}
```

`InitializeScheduler` publica o `IServiceProvider` no contexto do scheduler (`scheduler.Context["Quartz.ServiceProvider"] = serviceProvider`), amarra listeners/calendars (filtrando por `OptionsName`) e chama `processor.ScheduleJobs(scheduler)` para materializar os jobs/triggers acumulados no `QuartzOptions`.

**7) `SchedulerBuilder` — seleção de store/threadpool por propriedades**

`Quartz/SchedulerBuilder.cs`
```csharp
public SchedulerBuilder UseInMemoryStore(Action<InMemoryStoreOptions>? options = null)
{
    SetProperty(StdSchedulerFactory.PropertyJobStoreType, typeof(RAMJobStore).AssemblyQualifiedNameWithoutVersion());
    options?.Invoke(new InMemoryStoreOptions(this));
    return this;
}

public SchedulerBuilder UsePersistentStore(Action<PersistentStoreOptions> options)
    => UsePersistentStore<JobStoreTX>(options);
```

Usa `AssemblyQualifiedNameWithoutVersion()` — o tipo é referenciado **sem versão de assembly**, resiliente a upgrades. Isso, junto com `[DynamicallyAccessedMembers]` nos genéricos, é o modelo do Quartz para *trimming*, mas ainda depende de reflection em runtime (contraste com o Guará AOT-first).

### Fluxo passo a passo (Quartz)

1. `services.AddQuartz(q => { q.UseInMemoryStore(); q.AddJob<Foo>(...); q.AddTrigger(...); })` — cria `SchedulerBuilder`, roda o callback (jobs/triggers viram `IConfigureOptions<QuartzOptions>` diferidos), registra suporte, copia propriedades para `QuartzOptions`.
2. `services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true)` — registra `QuartzHostedService` (só se há `ISchedulerFactory`) + `NamedSchedulerHostedService`.
3. No boot, `QuartzHostedService.StartAsync` chama `schedulerFactory.GetScheduler()` → `ServiceCollectionSchedulerFactory` faz `Initialize(options.Value.ToNameValueCollection())` (aqui os `IConfigureOptions` rodam e preenchem `_jobDetails`/`_triggers`), constrói o scheduler, amarra listeners/calendars, agenda jobs.
4. Se `AwaitApplicationStarted` (default), o `scheduler.Start()` é adiado até `ApplicationStarted`.
5. Shutdown: `StopAsync` → `scheduler.Shutdown(WaitForJobsToComplete)`.

---

## Comparação lado a lado

| Dimensão | Hangfire | Quartz.NET | Guará (specs 009/010/018) |
|---|---|---|---|
| Ponto de entrada | `AddHangfire(Action<IGlobalConfiguration>)` **+** `AddHangfireServer(...)` | `AddQuartz(Action<configurator>)` **+** `AddQuartzHostedService()` | `AddGuara(Action<GuaraOptions>)` → `IGuaraBuilder` **+** `AddGuaraServer()` |
| Modelo de config | **Estado estático global** (`GlobalConfiguration.Configuration`, `JobStorage.Current`, `JobActivator.Current`, `GlobalJobFilters`) + fluent `Use...` com efeitos colaterais | **Padrão Options** (`QuartzOptions : Dictionary`) + `SchedulerBuilder` fluente | **Padrão Options**, **sem estado estático** (invariante do repo) |
| Momento da config | Preguiçoso: 1ª resolução de `IGlobalConfiguration` | Preguiçoso: 1ª resolução de `IOptions<QuartzOptions>`/`GetScheduler` | Binding no startup + **`ValidateOnStart`** |
| Validação de opções | **Nenhuma embutida** (erros em runtime) | Gancho `IPostConfigureOptions<QuartzOptions>` **no-op** (sem validação real) | **`IValidateOptions` + `ValidateOnStart`** obrigatório (spec 018) — falha cedo |
| Seleção de storage | `UseStorage()` seta `JobStorage.Current` (estático) | `UseInMemoryStore()`/`UsePersistentStore()` setam propriedades | `Use...Storage()` registra `IStorage` no DI (um por provider) |
| Config via `appsettings.json` | Manual (usuário lê e passa) | **Nativo**: `AddQuartz(IConfiguration)` com flatten e schedulers nomeados | Convenção `Guara:{Componente}` → `{Componente}Options` (spec 018) |
| `IHostedService` (lifetime) | **Transient** (empilha vários servidores) | **Singleton** via `TryAddEnumerable` | **Singleton** (spec 010; um servidor) |
| Defer de start p/ app pronta | `IHostApplicationLifetime.ApplicationStarted` | Opção `AwaitApplicationStarted` (default `true`) | Não previsto explicitamente — **deve adotar** |
| Ativação de job / escopo DI | `JobActivator` + `AspNetCoreJobActivatorScope` (escopo por job) | `IJobFactory` = `MicrosoftDependencyInjectionJobFactory` (escopo por execução) | `IJobInvoker` gerado (source gen, spec 029) + escopo |
| Múltiplas instâncias | Múltiplos servidores (vários `AddHangfireServer`) | Múltiplos schedulers **nomeados** | Um servidor (DD-3 spec 010) |
| Reflection / AOT | Reflection pesado | Reflection + `AssemblyQualifiedName` + `[DynamicallyAccessedMembers]` (trim-aware, não AOT-first) | **Zero reflection**, source gen, **AOT/Trim-safe** (invariante) |
| Ordem de chamadas | `AddHangfire` antes de `AddHangfireServer`/dashboard (`ThrowIfNotConfigured`) | `AddQuartz` **antes** de `AddQuartzHostedService` | `AddGuara()` primeiro; storage antes do server (validar no startup) |
| Namespace da extensão | `Hangfire` | `Quartz` | **`Microsoft.Extensions.DependencyInjection`** (ADR-0006) |
| Verificação "configurado?" | `ThrowIfNotConfigured` (marcador `IGlobalConfiguration`) | Presença de `ISchedulerFactory` no `IServiceCollection` | **Deve** ter marcador/serviço-sentinela + erro claro (spec 009 AC-4) |
| Acesso a `IServiceProvider` na config | `AddHangfire(Action<IServiceProvider, IGlobalConfiguration>)` | `AddQuartz(Action<configurator, IServiceProvider>)` **diferido** (`DeferredQuartzConfiguration`) | Não previsto — **pode adotar** para connection strings |

---

## O que o Guará já faz / deve adotar / pode melhorar

### Já faz (estado atual em `guara/src/`)

- **`IGuaraBuilder`** já existe (`Guara.Abstractions/IGuaraBuilder.cs`) expondo apenas `IServiceCollection Services` — raiz da API fluente, como manda o ADR-0006.
- **Uma extensão por pacote**, no namespace correto. Ex.: `AddGuaraWorker`, `AddGuaraDispatcher`, `UseMemoryStorage` — todas em `namespace Microsoft.Extensions.DependencyInjection` e devolvendo `IGuaraBuilder` para encadear.

`guara/src/Guara.Worker/WorkerServiceCollectionExtensions.cs`
```csharp
namespace Microsoft.Extensions.DependencyInjection; // namespace obrigatório (ADR-0006)

public static IGuaraBuilder AddGuaraWorker(this IGuaraBuilder builder, Action<WorkerOptions>? configure = null)
{
    ArgumentNullException.ThrowIfNull(builder);
    var options = new WorkerOptions();
    configure?.Invoke(options);
    builder.Services.TryAddSingleton(TimeProvider.System);
    builder.Services.TryAddSingleton(options);           // <-- POCO registrado direto, NÃO via Options pattern
    builder.Services.TryAddSingleton(sp => new GuaraWorker(/* deps resolvidas por lambda */));
    // ...
    return builder;
}
```

- **Zero reflection já na prática**: os registros usam **lambdas de factory explícitas** (`sp => new GuaraWorker(...)`), sem `ActivatorUtilities`/scan — alinhado com AOT.
- **`TimeProvider` injetado** e `TryAddSingleton` para idempotência de registro (evita "registro duplicado", spec 009 Edge Cases).
- **`UseMemoryStorage`** registra `IStorage` no DI (`Guara.Storage.Memory/MemoryStorageExtensions.cs`) — modelo "um `Use...()` por provider".

### Deve adotar

1. **Padrão Options de verdade (spec 018).** Hoje as opções são POCOs via `TryAddSingleton(options)` — **não** há `services.Configure<T>()`, `IOptions<T>`, `IValidateOptions<T>` nem `ValidateOnStart()`. A spec 018 (AC-1/AC-2/AC-5) e a 009 (AC-3) exigem binding por convenção (`Guara:Worker:MaxConcurrency`), validação no startup e reload via `IOptionsSnapshot`. **Adotar o modelo do Quartz** (`services.Configure<TOptions>(section)` + `IPostConfigureOptions`) mas com validação **real** (o gancho `QuartzConfiguration` do Quartz é no-op — não copiar essa lástima). Implementar `GuaraConfigurationExtensions.BindOptions<TOptions>(sectionName)` (assinatura já na spec 018) e chamar `.ValidateDataAnnotations().ValidateOnStart()`.

2. **`AddGuara()` central + `GuaraOptions` + `IGuaraBuilder` concreto (spec 009).** Ainda **não existem** (`Guara.Hosting` não está em `src/`). É o análogo ao par `AddHangfire`/`AddQuartz`. Deve registrar núcleo (pipeline, event bus, state machine, `IGuaraClient`) e devolver o `IGuaraBuilder` concreto. `GuaraOptions` (`ApplicationName`, `DefaultQueues`) validado por `IValidateOptions` no startup.

3. **`AddGuaraServer()` + `IHostedService` (specs 009/010).** Análogo a `AddHangfireServer`/`AddQuartzHostedService`. Registrar o hosted service como **Singleton** (seguir Quartz, **não** o Transient do Hangfire — o Guará tem um servidor por processo, DD-3 da spec 010). O `IHostedService` delega a `IGuaraServer.StartAsync/StopAsync`.

4. **Defer de start para `ApplicationStarted`.** Tanto Hangfire (`ApplicationStarted.Register(InitializeProcessingServer)`) quanto Quartz (`AwaitApplicationStarted`, default `true`) **não iniciam o processamento durante o startup da aplicação**. O Guará deve fazer o mesmo: injetar `IHostApplicationLifetime`, iniciar os motores em `ApplicationStarted` e retornar `Task.CompletedTask` de `StartAsync` (não bloquear o boot). A spec 010 (AC-1) não menciona isso — **vale acrescentar**.

5. **Erro claro quando falta storage / falta config (spec 009 AC-4, DD-2).** Hangfire usa o marcador `IGlobalConfiguration` + `ThrowIfNotConfigured`; Quartz confia na ausência de `ISchedulerFactory`. O Guará deve ter um **serviço-sentinela** registrado por `AddGuara()` e um `IValidateOptions`/checagem no startup que falhe com "chame `Use...Storage()`" se nenhum `IStorage` foi registrado, e que trate **dois storages** (DD-2: erro de ambiguidade).

6. **Shutdown gracioso com timeout (spec 010 AC-3).** Espelhar `QuartzHostedService.StopAsync` (`Task.WhenAny(startupTask, Task.Delay(timeout))` + shutdown final) e o `ShutdownDrainTimeout` já presente em `WorkerOptions`. Registrar sinal de parada em `ApplicationStopping` (como Hangfire faz com `SendStopSignal`).

### Pode melhorar (além dos dois)

7. **Configuração diferida com `IServiceProvider` (opcional).** O overload `AddQuartz(Action<configurator, IServiceProvider>)` do Quartz (via `DeferredQuartzConfiguration : IConfigureOptions<QuartzOptions>`) permite resolver serviços (ex.: connection string de um `IConfiguration`/secret store) **na hora de configurar**. Útil para os providers de storage do Guará. Adotar como overload opcional de `Use...Storage()` — sem tornar a superfície padrão maior (ADR-0006).

8. **Idempotência total via `TryAdd*` + `TryAddEnumerable`.** O Guará já usa `TryAddSingleton`. Para coleções (ex.: middlewares, listeners, múltiplos `IHostedService`), usar `TryAddEnumerable` como o Quartz faz com `IPostConfigureOptions`/`IHostedService` — evita registros duplicados quando o usuário chama um `Add...` duas vezes (spec 009 Edge Case "registro manual duplicado").

9. **Escopo de DI por execução de job.** Ambos criam um escopo por job (`AspNetCoreJobActivatorScope` / `MicrosoftDependencyInjectionJobFactory`). O `IJobInvoker` gerado (spec 029) deve abrir um `IServiceScope` por execução e liberá-lo (com suporte a `IAsyncDisposable`, como o `AspNetCoreJobActivatorScope`) — para jobs que dependem de serviços scoped (ex.: `DbContext`).

10. **Composição/ordem validada, não presumida.** O Quartz tem a armadilha silenciosa "`AddQuartz` deve vir antes de `AddQuartzHostedService`". O Guará pode **validar a ordem no startup** (via marcador) e emitir mensagem clara em vez de falhar obscuramente no DI — melhorando sobre os dois.

---

## Armadilhas e detalhes sutis a não perder na implementação

1. **Config preguiçosa vs. no-momento-do-Add.** No Hangfire o callback de `AddHangfire` **não roda em `AddHangfire`** — roda na 1ª resolução de `IGlobalConfiguration`. No Quartz, o callback de `AddQuartz` roda **na hora** para propriedades/`SchedulerBuilder`, mas **jobs/triggers/listeners são diferidos** como `IConfigureOptions<QuartzOptions>` e só materializam quando `IOptions<QuartzOptions>.Value` é lido (dentro de `GetScheduler`). Decidir conscientemente o momento no Guará e **documentar** — misturar os dois modelos gera bugs de ordem.

2. **Estado estático é veneno para testes e para múltiplos hosts.** O `JobStorage.Current`/`JobActivator.Current`/`GlobalJobFilters` do Hangfire são globais de processo: dois testes em paralelo ou dois `WebApplicationFactory` colidem. O Guará **proíbe** isso (invariante) — manter tudo em DI escopado ao container. Não recriar "atalhos estáticos por conveniência".

3. **Lifetime do `IHostedService`.** Hangfire usa **Transient** (para empilhar servidores); Quartz usa **Singleton** via `TryAddEnumerable`. Registrar Transient sem querer significa que o host pode instanciar o hosted service mais de uma vez em cenários específicos. Para o Guará (um servidor), usar **Singleton** e `TryAddEnumerable` para garantir registro único.

4. **`StartAsync` não deve bloquear o boot.** Ambos retornam rápido: Hangfire faz `return Task.CompletedTask` e adia o trabalho para `ApplicationStarted`; Quartz usa o truque `Task.Delay(Infinite, linkedToken)` + `ContinueWith(OnlyOnCanceled)` para "acordar" no `ApplicationStarted` **sem** lançar `OperationCanceledException`. Se o Guará iniciar loops pesados dentro de `StartAsync` de forma bloqueante, atrasa/quebra o startup do host.

5. **`ObjectDisposedException` no `StopAsync`.** O `BackgroundJobServerHostedService.StopAsync` engole `ObjectDisposedException` por causa de um bug do pacote de Testing do ASP.NET Core (StopAsync chamado 2x, dotnet/aspnetcore#40271). Se o Guará for testado com `WebApplicationFactory`, pode topar com o mesmo — prever idempotência no stop.

6. **Race entre start e stop.** `QuartzHostedService.StartSchedulerAsync` checa `applicationLifetime.ApplicationStopping.IsCancellationRequested` **antes** de iniciar — se o app já está parando, não inicia. O Guará precisa da mesma guarda para não subir motores durante um shutdown imediato.

7. **Ordem de aplicação de defaults vs. callback do usuário.** No `AddHangfire`, o default de log/activator é setado **antes** do callback, então o usuário sobrescreve. Se o Guará aplicar defaults **depois**, o usuário perde a customização. Definir a ordem explicitamente.

8. **`AwaitApplicationStarted` mudou semântica (Quartz).** O default `true` significa "não roda jobs durante o startup". Migrações de versões antigas (que iniciavam inline) podem notar mudança de timing. Se o Guará adotar o defer, escolher um default e documentá-lo como comportamento canônico (`docs/semantics.md`).

9. **Nome de scheduler forçado (Quartz nomeado).** Em `AddQuartz(name, ...)`, o `PropertySchedulerInstanceName` é **re-forçado após** o callback do usuário, para não "derivar" via `SetProperty`. Lição para o Guará: identidade de nó/servidor (spec 010 `ServerNode`) deve ser autoritativa e não sobrescrevível por config solta.

10. **Inicialização única e thread-safe.** O `ServiceCollectionSchedulerFactory` usa `SemaphoreSlim(1,1)` para garantir que o scheduler é construído **uma vez** mesmo sob resoluções concorrentes de `GetScheduler`. O `IGuaraServer`/factory equivalente deve ter guarda semelhante (sem `lock` bloqueante em caminho async — usar `SemaphoreSlim`, coerente com a invariante "sem `.Result`/`.Wait()`").

11. **Validação que não valida.** O `QuartzConfiguration : IPostConfigureOptions<QuartzOptions>.PostConfigure` do Quartz é **literalmente vazio** — o gancho existe mas não valida nada. A spec 018 do Guará (AC-2) exige validação **efetiva** (ex.: `MaxConcurrency == 0` deve falhar no boot). **Não** replicar o no-op: implementar `IValidateOptions<TOptions>` com regras reais + `ValidateOnStart()`.

12. **`appsettings` e segredos.** O flatten do Quartz (`Scheduler:InstanceName` → `quartz.scheduler.instanceName`) mostra o cuidado com convenção de chaves; o Guará usa `Guara:{Componente}`. Atenção à spec 018 (AC-4/DD-2): **connection strings nunca em logs**; usar `GetConnectionString`/secret store como o `ServiceCollectionSchedulerFactory.GetNamedConnectionString` (lê de `IConfiguration.GetConnectionString`) — e jamais logar o valor.

13. **`TryAddEnumerable` para `IPostConfigureOptions`/validators.** O Quartz registra `IPostConfigureOptions<QuartzOptions>` via `TryAddEnumerable` para garantir **uma** instância mesmo com múltiplos `AddQuartz`. Ao registrar validadores/pós-config no Guará, usar o mesmo para não rodar a validação N vezes.

14. **Dispose async do escopo de job.** O `AspNetCoreJobActivatorScope.DisposeScope` trata `IAsyncDisposable` (`DisposeAsync().GetAwaiter().GetResult()`), pois o dispose corre em thread dedicada. Se o Guará abrir escopo por execução, tratar `IAsyncDisposable` (comum com `DbContext` no .NET moderno) — senão, vazamento/deadlock potencial.

15. **Não confundir "adicionar serviços" com "iniciar".** A separação `AddHangfire`/`AddHangfireServer` e `AddQuartz`/`AddQuartzHostedService` existe para permitir **processos só-cliente** (que só enfileiram, sem processar). A spec 009 (DD-1) do Guará adota isso: `AddGuara()` **não** inclui o servidor; `AddGuaraServer()` é explícito. Preservar essa separação — não "ligar o servidor por conveniência" dentro de `AddGuara()`.
