# Convenções de Nomenclatura

O Guará prefere **convenção a documentação extensa**. Seguir estas regras torna o código previsível o suficiente para dispensar explicação.

## Tipos

| Elemento | Convenção | Exemplo |
|---|---|---|
| Interface principal do componente | `I{Componente}` | `IWorker`, `IScheduler`, `IStorage`, `IExecutor`, `IDispatcher` |
| Contrato de recurso | `I{Recurso}{Papel}` | `IJobStorage`, `IQueueStorage`, `ILockProvider`, `ITransaction` |
| Provider (implementação) | `{Tecnologia}{Contrato}` | `SqlServerStorage`, `RedisLockProvider`, `MongoJobStorage` |
| Opções de configuração | `{Componente}Options` | `SchedulerOptions`, `WorkerOptions`, `StorageOptions` |
| Middleware do pipeline | `{Etapa}Middleware` | `ValidationMiddleware`, `RetryMiddleware`, `MetricsMiddleware` |
| Evento | `{Substantivo}{ParticípioPassado}` | `JobCreated`, `JobScheduled`, `JobCompleted`, `WorkerRequested` |
| Estado de Job | `{Nome}State` ou enum `JobState` | `JobState.Enqueued`, `JobState.Processing` |
| Builder fluente | `{Componente}Builder` | `GuaraBuilder`, `StorageBuilder` |

## Pacotes e Namespaces

| Regra | Exemplo |
|---|---|
| Pacote do componente | `Guara.{Componente}` → `Guara.Scheduler` |
| Pacote de provider | `Guara.{Contrato}.{Tecnologia}` → `Guara.Storage.PostgreSql` |
| Namespace do código | acompanha o pacote → `namespace Guara.Scheduler;` |
| **Namespace das extensões de DI** | **sempre** `Microsoft.Extensions.DependencyInjection` |

Colocar as extensões em `Microsoft.Extensions.DependencyInjection` faz `AddGuara...()` aparecer no IntelliSense logo após `builder.Services.` sem `using` extra — igual ao ecossistema .NET. Ver [ADR-0006](adr/0006-uma-extensao-addguara-por-pacote.md).

## Extensões de DI — a regra do "um método por pacote"

Cada pacote expõe **exatamente um** ponto de entrada. Sem sobrecargas espalhadas, sem métodos auxiliares públicos.

```csharp
// Guara.Hosting
builder.Services.AddGuara();

// Providers de storage — verbo "Use"
builder.Services.AddGuara()
    .UseSqlServerStorage(connectionString)
    .UseRedisStorage(redisOptions);

// Componentes adicionais — verbo "Add"
builder.Services.AddGuaraServer();
builder.Services.AddGuaraDashboard();
builder.Services.AddGuaraWorker();
builder.Services.AddGuaraOpenTelemetry();
```

Convenção de verbos:

- `AddGuara...()` → **liga** um componente/capacidade.
- `Use...Storage()` / `Use...()` → **seleciona** uma implementação de um ponto de extensão.

## Regras rígidas

- **Toda API pública deve ser pequena.** Se um pacote precisa de mais de um método público de entrada, provavelmente são dois componentes.
- **Nunca** factories globais estáticas. **Nunca** singletons estáticos. Tudo passa por DI. Ver [anti-patterns.md](anti-patterns.md).
- Nomes de eventos no passado (`JobCompleted`), pois descrevem algo que **já ocorreu**.
- Providers levam o nome da tecnologia como prefixo, não sufixo (`SqlServerStorage`, não `StorageSqlServer`).
