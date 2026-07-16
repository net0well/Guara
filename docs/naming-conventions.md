# Convenções de Nomenclatura

O Guará prefere **convenção a documentação extensa**. Seguir estas regras torna o código previsível o suficiente para dispensar explicação.

## Idioma: API do usuário em português ([ADR-0010](adr/0010-api-do-usuario-em-portugues.md))

Os **métodos que o usuário chama** para operar jobs são em **português**; todo o resto segue o ecossistema .NET em inglês.

| Superfície | Idioma | Exemplos |
|---|---|---|
| Métodos do `IGuaraClient` / `IBatchClient` (Pro) | **Português** | `EnfileirarAsync`, `AgendarAsync`, `AdicionarOuAtualizarRecorrenteAsync`, `ExcluirAsync`, `ContinuarComAsync` |
| Atributos de job ([Spec 036](../spec/036-atributos-de-job.md)) | **Português**, prefixo `Guara` | `[GuaraFila]`, `[GuaraRetentativas]`, `[GuaraDesabilitarConcorrencia]`, `[GuaraTempoLimite]`, `[GuaraPularSeAnteriorEmExecucao]` |
| Sufixo assíncrono | Inglês (convenção .NET) | `...Async` sempre |
| Extensões de DI ([ADR-0006](adr/0006-uma-extensao-addguara-por-pacote.md)) | Inglês | `AddGuara()`, `UsePostgreSqlStorage()`, `MapGuaraDashboard()` |
| Tipos, contratos, eventos, options, atributos | Inglês | `IGuaraClient`, `JobId`, `JobCompleted`, `WorkerOptions`, `[GuaraJob]` |
| Rotas HTTP, permissões, CLI, config | Inglês | `/api/v1/jobs`, `guara:view`, `guara jobs retry` |

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
