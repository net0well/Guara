# Regras de Dependência

As três leis abaixo são **enforçadas por `Guara.Analyzers`** (build quebra se violadas), não apenas por convenção.

## Lei 1 — Um projeto, uma responsabilidade

Nunca misturar responsabilidades num mesmo pacote.

| Errado | Correto |
|---|---|
| `Storage + Dashboard` | `Storage`, `Dashboard` (separados) |
| `Storage + Scheduler` | `Storage`, `Scheduler` |
| `Scheduler + Worker` | `Scheduler`, `Worker` |

## Lei 2 — Dependências unidirecionais

```
Dashboard  →  Api  →  Core  →  Abstractions
```

Nunca o contrário. `Abstractions` é a base e **não depende de nada**. Cada seta é "conhece / referencia".

### Direção por camada conceitual

```
                Guara.Abstractions          (contratos puros — folha da árvore)
                        ▲
                Guara.Core                  (modelos, estados, pipeline)
                        ▲
        ┌───────────────┼─────────────────────────────┐
   Guara.Scheduler  Guara.Worker  Guara.Dispatcher  Guara.Executor   (motores)
        ▲               ▲
   Guara.Hosting  /  Guara.Server           (composição e lifecycle)
        ▲
   Guara.Dashboard(.Api)  /  Guara.Cli      (superfícies externas)

   Providers (Guara.Storage.*, Guara.OpenTelemetry, ...) dependem só de
   suas abstrações (Guara.Storage, Guara.Diagnostics) — nunca dos motores.
```

## Lei 3 — Comunicação só por contrato

Nenhum componente conversa diretamente com outro. Toda comunicação passa por **interface** (chamada síncrona desacoplada) ou **evento** (notificação assíncrona).

```
Scheduler  →  IStorage  →  SqlServerStorage
```

Nunca:

```
Scheduler  →  SqlServerStorage        ❌ acopla motor a provider concreto
Dispatcher →  Executor (classe)       ❌ deveria emitir WorkerRequested/evento
```

### Contratos centrais

Vivem em `Guara.Abstractions`, **exceto** a família Storage, que vive em `Guara.Storage` (pacote de contratos, conforme `Filosofia.md`).

| Contrato | Vive em | Dono conceitual | Implementado por |
|---|---|---|---|
| `IStorage`, `IJobStorage`, `IQueueStorage`, `ITransaction`, `ILockProvider` | `Guara.Storage` | `Guara.Storage` | `Guara.Storage.*` |
| `IScheduler` | `Guara.Abstractions` | `Guara.Scheduler` | `Guara.Scheduler` |
| `IDispatcher` | `Guara.Abstractions` | `Guara.Dispatcher` | `Guara.Dispatcher` |
| `IWorker` | `Guara.Abstractions` | `Guara.Worker` | `Guara.Worker` |
| `IExecutor` | `Guara.Abstractions` | `Guara.Executor` | `Guara.Executor` |
| `ISerializer` | `Guara.Abstractions` | `Guara.Serialization` | `Guara.Serialization` |
| `IEventPublisher`, `IEventHandler<TEvent>` | `Guara.Abstractions` | `Guara.Core` | `Guara.Core` |
| Logging/métricas/tracing | BCL (`Microsoft.Extensions.Logging`, `System.Diagnostics.Metrics`) | — | `Guara.Diagnostics`, `Guara.OpenTelemetry` |

## Como validar antes do commit

```bash
# grafo de dependências não deve conter arestas "de baixo para cima"
dotnet build /p:EnforceGuaraLayering=true
```

Se `Guara.Analyzers` acusar `GUARA0001` (dependência invertida) ou `GUARA0002` (referência a implementação concreta), a build falha. Ver [anti-patterns.md](anti-patterns.md).
