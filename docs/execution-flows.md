# Fluxos de Execução

Dois fluxos ortogonais governam o Guará: a **comunicação entre componentes** (por eventos) e o **processamento de cada Job** (por pipeline de middlewares).

## Fluxo de Eventos (entre componentes)

Nenhum componente chama outro diretamente. Cada um reage a um evento e emite o próximo.

```
JobCreated
   ↓
Scheduler          calcula NextRun / Cron / Delay
   ↓
JobScheduled
   ↓
Dispatcher         busca o Job elegível na fila
   ↓
WorkerRequested
   ↓
Worker             aloca capacidade e dispara
   ↓
ExecutorStarted
   ↓
Executor           executa o Job pronto
   ↓
JobCompleted  (ou JobFailed → Retry)
```

| Evento | Emitido por | Consumido por |
|---|---|---|
| `JobCreated` | API/cliente (`Enqueue`, `Schedule`) | `Scheduler` |
| `JobScheduled` | `Scheduler` | `Dispatcher` |
| `WorkerRequested` | `Dispatcher` | `Worker` |
| `ExecutorStarted` | `Worker` | `Executor` |
| `JobCompleted` / `JobFailed` | `Executor` | `Diagnostics`, `Scheduler` (recurring), notificações |

Eventos trafegam por filas internas baseadas em `Channel<T>` — ver [ADR-0004](adr/0004-channel-para-filas-internas.md).

### Aviso de trabalho (wakeup)

Paralelo ao fluxo de eventos, e no sentido inverso: quem torna um job **elegível agora** avisa a fila por `IQueueSignal`, e o `Dispatcher` — que estaria dormindo com a fila vazia — acorda na hora em vez de esperar o próximo ciclo de busca.

```
EnfileirarAsync  →  IQueueSignal.SignalAsync(fila)  →  Dispatcher acorda  →  busca
```

| Regra | Consequência |
|---|---|
| O aviso é **best-effort** | Perdê-lo atrasa a busca até o ciclo periódico, nunca perde o job |
| `PollingInterval` é o **teto**, não o ritmo | O ciclo periódico é o piso que cobre o que se torna elegível sozinho (retentativa vencida, lease abandonado) |
| Só se avisa o que já é elegível | Retentativa, reagendamento e continuação pendente têm data futura |
| O aviso emitido sem ninguém aguardando é **retido** | Fecha a corrida entre a última busca e o início da espera |

O padrão é `InProcessQueueSignal` (nó único, sem infraestrutura). Trocar o registro por um transporte externo dá alcance entre nós. Ver [ADR-0012](adr/0012-wakeup-por-sinal-de-fila.md).

## Pipeline do Job (dentro do Executor)

Cada etapa é um **middleware**, no modelo do ASP.NET Core: recebe o contexto e um `next`. A ordem é fixa.

```
Validation → Authorization → Serialization → Middleware (custom)
           → Metrics → Logging → Retry → Executor → Success → Notifications
```

| Etapa | Middleware | Papel |
|---|---|---|
| Validation | `ValidationMiddleware` | Valida o payload/args do Job |
| Authorization | `AuthorizationMiddleware` | Verifica permissão de execução |
| Serialization | `SerializationMiddleware` | (De)serializa argumentos via `ISerializer` |
| Middleware | *custom* | Ponto de extensão do usuário |
| Metrics | `MetricsMiddleware` | Contadores/histogramas via `IMetrics` |
| Logging | `LoggingMiddleware` | Log estruturado via `ILogger` |
| Retry | `RetryMiddleware` | Política de retentativa/back-off |
| Executor | `ExecutionMiddleware` | Invoca o método do Job |
| Success | `SuccessMiddleware` | Marca estado final de sucesso |
| Notifications | `NotificationMiddleware` | Dispara notificações pós-execução |

Assinatura conceitual (ver exemplo completo em [patterns.md](patterns.md)):

```csharp
public interface IJobMiddleware
{
    ValueTask InvokeAsync(JobContext context, JobDelegate next, CancellationToken ct);
}
```

## Ciclo de Vida / Estados do Job

```
Created → Enqueued → Scheduled → Processing → (Succeeded | Failed)
                                        ↑            │
                                        └── Retrying ┘   (se RetryMiddleware permitir)
```

| Estado | Significado |
|---|---|
| `Created` | Job aceito, ainda não enfileirado |
| `Enqueued` | Na fila, aguardando dispatch |
| `Scheduled` | Com `NextRun` calculado (delay/cron/recurring) |
| `Processing` | Em execução no pipeline |
| `Succeeded` | Concluído com sucesso |
| `Failed` | Falhou e esgotou retentativas |
| `Retrying` | Falhou e será reexecutado |

## Regras transversais dos fluxos

- **`CancellationToken` sempre propagado** por todo o pipeline e chamadas de storage.
- Efeito colateral externo já concluído **não** deve ser revertido por cancelamento tardio: a persistência de estado final usa um token não-cancelável quando o efeito já ocorreu (padrão análogo ao do domínio HTTP).
- Recurring jobs: ao `JobCompleted`, o `Scheduler` recalcula o próximo `NextRun` e emite novo `JobScheduled`.
- Cluster: a **execução de jobs é distribuída** — todo nó busca e executa, coordenado por posse individual (lease). Só o que não se divide roda sob liderança: promoção de recorrentes e manutenção. Ver `Guara.Cluster` em [components.md](components.md).
