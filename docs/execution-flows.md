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

O pipeline é **curto de propósito**: middlewares registrados como `IJobMiddleware`, e a invocação do job no fim. Tudo que precisa acontecer *em volta* da execução — exclusão mútua, tempo limite, persistência do desfecho, retentativa, eventos — é feito pelo próprio `GuaraExecutor`, e **não** como middleware.

```
[middlewares registrados, na ordem de registro] → invocação do job
```

O motivo é que essas etapas não são opcionais nem reordenáveis: um middleware de "persistir sucesso" que alguém removesse ou pusesse fora de ordem quebraria a garantia de entrega. O que é ponto de extensão fica no pipeline; o que é invariante fica no executor.

| Registrado por | Middleware | Papel |
|---|---|---|
| `UseGuaraDiagnostics()` | `TracingMiddleware` | `Activity` por execução |
| `UseGuaraDiagnostics()` | `LoggingMiddleware` | Log estruturado via `ILogger` |
| `UseGuaraDiagnostics()` | `MetricsMiddleware` | Contadores/histogramas |
| você | *custom* | Qualquer `IJobMiddleware` que você registre |

O que o **executor** faz em volta do pipeline, na ordem:

1. Carrega o job e seus metadados declarados (atributos lidos em compilação).
2. Adquire a chave de `[GuaraDesabilitarConcorrencia]`, se houver, e **renova enquanto o job roda**; perder a chave aborta a execução local.
3. Aplica `[GuaraTempoLimite]` cancelando o token do job.
4. Roda o pipeline.
5. Persiste o desfecho com token **não-cancelável** e publica o evento.
6. Em falha, decide entre `Retrying` (reagendado com back-off) e `Failed`, pela política do job.

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
                          ↑             ↑            │
                          │             └── Retrying ┘   (tentativas restantes)
                          └───────────────────────────   (chave de exclusão ocupada:
                                                          volta à fila sem consumir tentativa)
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
