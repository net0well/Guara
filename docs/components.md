# Componentes

Cada linha da tabela é um projeto em `src/`. **Um projeto = uma responsabilidade.** Se algo não couber na coluna "Responsabilidade", é um componente novo — nunca um apêndice de outro.

## Catálogo

| Componente | Responsabilidade | Conhece | Nunca conhece |
|---|---|---|---|
| `Guara.Abstractions` | Contratos transversais (`IScheduler`, `IWorker`, `IExecutor`, `IDispatcher`, `ILeaderElection`), eventos, value types e abstrações de pipeline | — (nada) | qualquer implementação |
| `Guara.Core` | Modelos internos, estados de Job, pipeline, abstrações comuns | `Abstractions` | banco, ASP.NET, Dashboard |
| `Guara.Hosting` | `AddGuara()`, configuração, DI, bootstrap, `HostedService`s | `Core`, `Abstractions` | providers concretos |
| `Guara.Server` | Lifecycle do servidor: inicia workers e scheduler, heartbeat | `Core`, `Abstractions` | como um Job executa por dentro |
| `Guara.Scheduler` | Calcula Cron, Delay, Recurring, Next Run | `Abstractions` | execução, storage concreto |
| `Guara.Worker` | **Apenas** executa Jobs | `Abstractions` | agendamento, dispatch |
| `Guara.Dispatcher` | **Apenas** busca Jobs | `Abstractions` | execução, agendamento, serialização |
| `Guara.Executor` | Recebe Job pronto → executa → atualiza estado → finaliza | `Abstractions` | como o Job foi buscado/agendado |
| `Guara.Storage` | Define `IStorage`, `IJobStorage`, `IQueueStorage`, `ILockProvider` e o handle `RelationalTransaction` | `Abstractions` | qualquer banco específico |
| `Guara.Storage.Memory` | Implementa `IStorage` em memória | `Storage` | outros componentes |
| `Guara.Storage.SqlServer` | Implementa `IStorage` sobre SQL Server | `Storage` | Scheduler, Worker, Dashboard |
| `Guara.Storage.PostgreSql` | Implementa `IStorage` sobre PostgreSQL | `Storage` | idem |
| `Guara.Storage.MySql` | Implementa `IStorage` sobre MySQL | `Storage` | idem |
| `Guara.Storage.Mongo` | Implementa `IStorage` sobre MongoDB | `Storage` | idem |
| `Guara.Redis` | Implementa `IQueueSignal` sobre pub/sub — leva o aviso de trabalho entre nós. **Não é storage** ([ADR-0013](adr/0013-redis-como-acelerador.md)) | `Abstractions`, `Core` | storage, motores |
| `Guara.Diagnostics` | Logging, Metrics, Tracing, HealthChecks | `Abstractions` | providers de storage |
| `Guara.OpenTelemetry` | Exporters OpenTelemetry | `Diagnostics`, `Abstractions` | lógica de negócio |
| `Guara.Authorization` | Autorização de jobs e dashboard | `Abstractions` | autenticação concreta |
| `Guara.Authentication` | Autenticação | `Abstractions` | autorização concreta |
| `Guara.Dashboard` | Composição do dashboard | `Dashboard.Api`, `Abstractions` | storage concreto |
| `Guara.Dashboard.Api` | Fornece APIs para o Dashboard | `Abstractions` | **renderizar HTML** |
| `Guara.Dashboard.Angular` | SPA Angular | a API HTTP | qualquer detalhe interno |
| `Guara.Configuration` | Binding e validação de opções | `Abstractions` | providers |
| `Guara.Extensions` | Extensões utilitárias transversais | `Abstractions` | — |
| `Guara.Cluster` | Eleição de líder com posse renovada e mantida entre ciclos, sobre o lock distribuído do storage; os papéis liderados aparecem em `ServerNode.Roles` e no painel ([ADR-0017](adr/0017-eleicao-de-lider.md)). Políticas de failover configuráveis ainda não | `Abstractions`, `Storage` | storage concreto |
| `Guara.Cli` | Ferramenta de linha de comando | `Hosting`, `Abstractions` | internals de providers |
| `Guara.Analyzers` | Analisadores Roslyn que enforçam as regras deste doc | — | runtime do framework |
| `Guara.SourceGenerators` | Geram descoberta/registro sem reflection | — | runtime do framework |

## Fronteiras de Responsabilidade (as mais confundidas)

O ciclo de um Job atravessa **quatro** componentes distintos. Nunca os funda:

| Componente | Faz | **Não** faz |
|---|---|---|
| `Guara.Scheduler` | Decide **quando** (Cron/Delay/Recurring/NextRun) | executar, buscar, persistir |
| `Guara.Dispatcher` | Decide **o quê** buscar da fila | executar, agendar, serializar |
| `Guara.Worker` | Aloca capacidade e **dispara** a execução | agendar, buscar, calcular retry |
| `Guara.Executor` | **Executa** o Job pronto e atualiza estado | buscar, agendar |

Combinações proibidas (ver [anti-patterns.md](anti-patterns.md)): `Storage + Dashboard`, `Storage + Scheduler`, `Scheduler + Worker`.

## Adicionando um Componente

1. Confirme que a responsabilidade não existe em nenhum componente atual.
2. Defina a interface principal em `Guara.Abstractions`.
3. Crie `src/Guara.{Componente}` com **um** `AddGuara...()`.
4. Crie `tests/Guara.{Componente}.Tests`.
5. Se for caminho crítico, crie `benchmarks/Guara.{Componente}.Benchmarks`.
6. Registre um [ADR](adr/README.md) se a decisão for estrutural.
7. Rode o [checklist](checklist.md).
