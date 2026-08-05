# Padrões Obrigatórios

Todo componente do Guará segue os mesmos cinco padrões. Eles são a razão de a documentação poder ser curta: o formato é sempre o mesmo.

## 1. Anatomia de um Componente

Um componente é composto por locais fixos:

```
Guara.Abstractions/
  IScheduler.cs                      # contrato principal (público)
  Events/JobScheduled.cs             # eventos que emite

src/Guara.Scheduler/
  SchedulerOptions.cs                # configuração
  Scheduler.cs                       # implementação (internal quando possível)
  DependencyInjection/
    SchedulerServiceCollectionExtensions.cs   # o ÚNICO AddGuara...()

tests/Guara.Scheduler.Tests/
benchmarks/Guara.Scheduler.Benchmarks/   # se caminho crítico
```

Regras:
- A **interface** é pública e vive em `Guara.Abstractions`.
- A **implementação** é `internal` sempre que possível — o mundo externo depende do contrato, não da classe.
- Um componente **nunca** referencia a implementação de outro.

## 2. Extensão de DI (um método por pacote)

```csharp
// src/Guara.Scheduler/DependencyInjection/SchedulerServiceCollectionExtensions.cs
namespace Microsoft.Extensions.DependencyInjection; // <-- sempre este namespace

public static class SchedulerServiceCollectionExtensions
{
    public static IGuaraBuilder AddGuaraScheduler(
        this IGuaraBuilder builder,
        Action<SchedulerOptions>? configure = null)
    {
        builder.Services.Configure(configure ?? (_ => { }));
        builder.Services.AddSingleton<IScheduler, Scheduler>();
        return builder; // retorna o builder para permitir fluência
    }
}
```

- **Nunca** `services.AddSingleton<IScheduler, Scheduler>()` espalhado pela aplicação — só dentro deste método.
- **Nunca** factory global estática nem singleton estático. Tudo por DI. Ver [anti-patterns.md](anti-patterns.md).

## 3. API Fluente (composição, inspirada no ASP.NET Core)

```csharp
builder.Services
    .AddGuara()                       // núcleo (Guara.Hosting)
    .UseSqlServerStorage(conn)        // seleciona um provider
    .AddGuaraServer()                 // lifecycle
    .AddGuaraScheduler()
    .AddGuaraWorker()
    .AddGuaraDashboard()
    .AddGuaraOpenTelemetry();
```

`AddGuara()` devolve `IGuaraBuilder`; cada extensão recebe e devolve `IGuaraBuilder`. Composição sobre herança.

## 4. Provider (ponto de extensão)

Contrato em `Guara.Storage`, implementação em `Guara.Storage.{Tecnologia}`:

```csharp
// Guara.Storage (define, não implementa)
public interface IJobStorage
{
    ValueTask<JobRecord?> AcquireNextAsync(CancellationToken ct);
    ValueTask UpdateStateAsync(JobId id, JobState state, CancellationToken ct);
}

// Guara.Storage.PostgreSql (implementa apenas o contrato)
internal sealed class PostgreSqlJobStorage(NpgsqlDataSource dataSource) : IJobStorage
{
    public ValueTask<JobRecord?> AcquireNextAsync(CancellationToken ct) { /* ... */ }
    public ValueTask UpdateStateAsync(JobId id, JobState state, CancellationToken ct) { /* ... */ }
}

// Guara.Storage.PostgreSql — a única extensão pública
namespace Microsoft.Extensions.DependencyInjection;
public static class PostgreSqlStorageExtensions
{
    public static IGuaraBuilder UsePostgreSqlStorage(this IGuaraBuilder builder, string connectionString)
    {
        builder.Services.AddSingleton<IJobStorage>(/* ... */);
        return builder;
    }
}
```

Trocar de provider = trocar **uma** linha (`UsePostgreSqlStorage` → `UseMongoStorage`). Nenhum motor muda. Ver [ADR-0003](adr/0003-abstracao-de-storage-por-provider.md).

## 5. Middleware do Pipeline

```csharp
public interface IJobMiddleware
{
    ValueTask InvokeAsync(JobContext context, JobDelegate next, CancellationToken ct);
}

internal sealed class RetryMiddleware(IOptions<RetryOptions> options) : IJobMiddleware
{
    public async ValueTask InvokeAsync(JobContext context, JobDelegate next, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { await next(context, ct); return; }
            catch (Exception ex) when (attempt < options.Value.MaxAttempts && !ct.IsCancellationRequested)
            {
                context.RecordRetry(attempt, ex);
                await Task.Delay(options.Value.BackOff(attempt), ct);
            }
        }
    }
}
```

Ordem das etapas é fixa e documentada em [execution-flows.md](execution-flows.md). Middlewares custom entram no slot `Middleware`.

## Comunicação por Evento (nunca chamada direta)

```csharp
// CORRETO — Dispatcher emite; não conhece o Worker
await eventBus.PublishAsync(new WorkerRequested(jobId), ct);

// ERRADO — acopla Dispatcher a Worker
await worker.RunAsync(job, ct);   // ❌
```

Ver [ADR-0002](adr/0002-comunicacao-por-eventos.md).
