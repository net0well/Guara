<p align="center">
  <img src="assets/logo-escrita.png" alt="Guará — Job Scheduler" width="440">
</p>

<p align="center">
  <strong>Agendamento e execução de jobs para .NET moderno — orientado a componentes, agnóstico a storage, pronto para AOT.</strong>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/licen%C3%A7a-LGPL--3.0-blue" alt="Licença: LGPL-3.0"></a>
  <img src="https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4" alt=".NET 8.0 | 10.0">
  <img src="https://img.shields.io/badge/status-em%20desenvolvimento%20ativo-orange" alt="Status: em desenvolvimento ativo">
</p>

<p align="center">
  <a href="README.md">English</a> | Português (Brasil)
</p>

---

> **Status do projeto.** O Guará está em desenvolvimento ativo e ainda não foi publicado no NuGet. A API pública mostrada abaixo reflete as especificações aprovadas (ver [`spec/`](spec/)) e pode mudar até a primeira versão estável. Dê star e acompanhe o repositório para seguir o progresso.

## O que é o Guará

O Guará é um **job scheduler open source para .NET**, no mesmo espaço de problema do Hangfire: jobs fire-and-forget, execução com atraso, jobs recorrentes por cron, retentativas automáticas, processamento distribuído entre nós e um dashboard em tempo real para observar e operar tudo.

O que o torna diferente é a forma como é construído:

- **Arquitetura orientada a componentes.** Não há camadas tradicionais. Cada responsabilidade — agendar, buscar, executar, armazenar, observar — é um componente independente com contratos, pacote, testes e ciclo de vida próprios. Componentes se comunicam apenas por interfaces e eventos, nunca referenciando implementações uns dos outros.
- **O storage é a fila.** Nenhum message broker é necessário. Os jobs vivem no seu banco de dados, e a aquisição atômica com lease (visibility timeout) garante que cada job seja processado por exatamente um worker, mesmo com muitos nós.
- **Zero dependências de terceiros no núcleo.** O runtime depende apenas da plataforma .NET (`Microsoft.Extensions.*`, `System.Text.Json`). Drivers de banco ficam isolados nos pacotes de provider. Até o parser de cron é nosso.
- **Zero reflection, pronto para Native AOT.** Descoberta, registro e invocação de jobs são gerados em tempo de compilação por source generators. O núcleo compila limpo sob trimming e `PublishAot`.
- **Logging estruturado sem lock-in.** O Guará loga via `Microsoft.Extensions.Logging` com propriedades estruturadas (`JobId`, `Queue`, `Attempt`, `State`...). Plugue o sink que quiser — JSON console, OpenTelemetry, Seq, ELK — o framework não impõe nada.

O nome vem do **lobo-guará**, animal veloz e resiliente nativo do Brasil. Made in Brasil.

## Recursos

| Capacidade | Descrição |
|---|---|
| Fire-and-forget | Enfileire um job e retorne imediatamente |
| Jobs com atraso | Execute uma vez após um intervalo |
| Jobs recorrentes | Expressões cron com suporte a fuso horário e horário de verão |
| Continuations | Encadeie jobs: B roda automaticamente quando A termina |
| Retentativas automáticas | Back-off exponencial, configurável por job, com opt-out para trabalho não idempotente |
| Dashboard em tempo real | SPA Angular alimentada por Server-Sent Events — mudanças de estado aparecem em cerca de um segundo |
| Processamento distribuído | Múltiplos nós, eleição de líder, failover, locks distribuídos — coordenados pelo próprio storage |
| Storage plugável | PostgreSQL, SQL Server, Redis, MongoDB, In-Memory — troque com uma linha |
| Observabilidade | Logs estruturados, métricas (`System.Diagnostics.Metrics`), traces (`ActivitySource`), health checks, exporters OpenTelemetry opcionais |
| Seguro por padrão | O dashboard nega acesso anônimo a menos que configurado explicitamente |

## Começando (API planejada)

Instale os pacotes (nomes finais, publicação pendente):

```bash
dotnet add package Guara
dotnet add package Guara.Storage.PostgreSql
```

Configure tudo com uma API fluente, no estilo do ASP.NET Core:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddGuara()
    .UsePostgreSqlStorage(builder.Configuration.GetConnectionString("guara")!)
    .AddGuaraServer()        // inicia workers, scheduler, heartbeat
    .AddGuaraDashboard();    // dashboard em tempo real

var app = builder.Build();

app.MapGuaraDashboard();     // serve a SPA + API em /guara
app.Run();
```

Enfileire jobs de qualquer lugar através do `IGuaraClient`.

> **API em português.** O Guará é um projeto brasileiro e os métodos de operação de jobs são em português, por decisão de identidade ([ADR-0010](docs/adr/0010-api-do-usuario-em-portugues.md)). O restante (tipos, extensões de DI, options, rotas) segue as convenções do ecossistema .NET em inglês.

```csharp
public sealed class ReportService(IGuaraClient jobs)
{
    public async Task SolicitarAsync(int clienteId, CancellationToken ct)
    {
        // Fire-and-forget
        await jobs.EnfileirarAsync(() => GerarRelatorioAsync(clienteId), ct);

        // Com atraso: roda uma vez, daqui a 24 horas
        await jobs.AgendarAsync(() => EnviarLembreteAsync(clienteId), TimeSpan.FromHours(24), ct);
    }

    public Task GerarRelatorioAsync(int clienteId) { /* ... */ }
    public Task EnviarLembreteAsync(int clienteId) { /* ... */ }
}
```

Jobs recorrentes usam expressões cron:

```csharp
await jobs.AdicionarOuAtualizarRecorrenteAsync(
    id: "limpeza-noturna",
    () => LimparRegistrosExpiradosAsync(),
    cron: "0 3 * * *",             // todo dia às 03:00
    TimeZoneInfo.Utc,
    ct);
```

Encadeie trabalho com continuations:

```csharp
var exportacao = await jobs.EnfileirarAsync(() => ExportarPedidosAsync(mes), ct);

// Roda automaticamente quando a exportação concluir com sucesso
await jobs.ContinuarComAsync(exportacao, () => NotificarExportacaoConcluidaAsync(mes), ct);
```

Retentativas são automáticas (3 tentativas com back-off exponencial por padrão). Jobs com efeitos colaterais irreversíveis podem desligar:

```csharp
[GuaraJob(MaxAttempts = 0)]   // nunca retenta: cobrar duas vezes seria pior que falhar
public async Task CobrarClienteAsync(int faturaId, CancellationToken ct) { /* ... */ }
```

## Como funciona

Cada componente reage a um evento e emite o próximo — nenhum componente chama outro diretamente:

```
JobCreated -> Scheduler -> JobScheduled -> Dispatcher -> WorkerRequested
           -> Worker -> ExecutorStarted -> Executor -> JobCompleted | JobFailed
```

Dentro do executor, cada job percorre um pipeline de middlewares, no modelo do ASP.NET Core:

```
Validation -> Authorization -> Serialization -> Middleware custom
           -> Metrics -> Logging -> Retry -> Execution -> Success -> Notifications
```

Cada etapa é um `IJobMiddleware`; o slot `Custom` é o ponto de extensão do usuário. A arquitetura completa — regras de dependência, convenções de nomenclatura, fluxos de execução e os ADRs por trás de cada decisão — está documentada em [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Providers de storage

O `Guara.Storage` define os contratos; cada provider os implementa usando as melhores primitivas do seu backend. Todos os providers precisam passar o mesmo kit de testes de conformidade.

| Provider | Dequeue atômico | Lock distribuído | Push em tempo real |
|---|---|---|---|
| PostgreSQL | `FOR UPDATE SKIP LOCKED` | Advisory locks | `LISTEN/NOTIFY` |
| SQL Server | `READPAST + UPDLOCK` | `sp_getapplock` | Polling |
| Redis | Scripts Lua | `SET NX PX` + TTL | Keyspace notifications |
| MongoDB | `findAndModify` | Coleção com TTL | Change streams |
| In-Memory | Estruturas lock-free | Local ao processo | Imediato |

Trocar de provider é uma mudança de uma linha. Capacidades que diferem entre backends (consultas ricas do dashboard, push server-side) são declaradas explicitamente e degradam de forma transparente.

## Dashboard

O dashboard é uma SPA Angular servida como assets estáticos embutidos — sem deploy separado, sem Node.js em runtime. Consome apenas a API HTTP versionada (`/guara/api/v1`) e atualiza em tempo real por Server-Sent Events. A autenticação integra com os esquemas do ASP.NET Core do host (cookie, JWT, OIDC), e cada ação é protegida por permissões granulares (`guara:view`, `guara:trigger`, `guara:retry`, `guara:delete`, `guara:view-payload`). Acesso anônimo é negado por padrão.

## Observabilidade

- **Logs**: estruturados, via `Microsoft.Extensions.Logging` — propriedades como `JobId`, `Queue`, `JobType`, `Attempt`, `DurationMs` em todo registro. O host de exemplo escreve JSON no stdout com o formatter nativo do console; use o sink que preferir.
- **Métricas**: `System.Diagnostics.Metrics` (`guara.jobs.processed`, `guara.jobs.failed`, `guara.job.duration`, `guara.queue.length`).
- **Traces**: um span de `Activity` por execução de job (`ActivitySource("Guara")`).
- **Health checks**: alcance do storage, liveness do servidor, limites de fila.
- **OpenTelemetry**: o pacote opcional `Guara.OpenTelemetry` registra as fontes do Guará no seu pipeline OTel existente. O núcleo nunca força um exporter.

## Roadmap

| Marco | Status |
|---|---|
| Documentação de arquitetura e ADRs | Concluído |
| Especificação completa (35 specs, uma por componente) | Concluído |
| Fundação: `Guara.Abstractions` + `Guara.Core` (pipeline, máquina de estados, eventos) | Concluído |
| Serialização e contratos de storage | Em andamento |
| Motores: Scheduler, Dispatcher, Worker, Executor | Planejado |
| Hosting, Server, provider PostgreSQL | Planejado |
| Dashboard (API + SPA Angular, tempo real) | Planejado |
| Demais providers, cluster, CLI, analyzers | Planejado |
| Primeira publicação no NuGet | Planejado |

## Documentação

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — filosofia arquitetural, regras de dependência, estrutura de pacotes, fluxos de execução, princípios de performance
- [`docs/adr/`](docs/adr/) — Architecture Decision Records
- [`spec/`](spec/) — a especificação completa, um documento por componente, com critérios de aceite
- [`Infra/`](Infra/) — deploy Docker de referência (PostgreSQL + proxy reverso)

## Contribuindo

O Guará está sendo construído com especificação primeiro: todo componente tem uma spec aprovada com critérios de aceite antes de qualquer código. Se quiser contribuir, comece lendo [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) e a spec do componente que interessa. Diretrizes de contribuição e templates de issue serão publicados antes da primeira release.

## Licença

O núcleo do Guará — tudo o que está documentado neste repositório — é licenciado sob **LGPL-3.0**, o que permite uso gratuito em aplicações comerciais e proprietárias via os pacotes NuGet padrão. Um pequeno conjunto de add-ons avançados (como orquestração de batches) está planejado como pacotes comerciais licenciados à parte, que ajudam a financiar o desenvolvimento do projeto; o núcleo permanecerá sempre gratuito e open source.
