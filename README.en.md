<p align="center">
  <img src="assets/logo-escrita.png" alt="Guará — Job Scheduler" width="440">
</p>

<p align="center">
  <strong>Background jobs and scheduling for modern .NET — component-based, storage-agnostic, AOT-ready.</strong>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-LGPL--3.0-blue" alt="License: LGPL-3.0"></a>
  <img src="https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4" alt=".NET 8.0 | 10.0">
  <img src="https://img.shields.io/badge/status-under%20active%20development-orange" alt="Status: under active development">
</p>

<p align="center">
  <a href="README.md">Português (Brasil)</a> | English
</p>

---

> **Project status.** Guará is under active development and has not been published to NuGet yet. The public API shown below reflects the approved specifications (see [`spec/`](spec/)) and may change until the first stable release. Star and watch the repository to follow the progress.

## What is Guará

Guará is an open source **job scheduler for .NET**, in the same problem space as Hangfire: fire-and-forget jobs, delayed execution, cron-based recurring jobs, automatic retries, distributed processing across nodes, and a real-time dashboard to observe and operate everything.

What makes it different is the way it is built:

- **Component-based architecture.** There are no traditional layers. Every responsibility — scheduling, dispatching, executing, storing, observing — is an independent component with its own contracts, package, tests, and lifecycle. Components communicate only through interfaces and events, never by referencing each other's implementations.
- **The storage is the queue.** No message broker required. Jobs live in your database, and atomic acquisition with leases (visibility timeout) guarantees each job is processed exactly by one worker, even across many nodes.
- **Zero third-party dependencies in the core.** The runtime depends only on the .NET platform (`Microsoft.Extensions.*`, `System.Text.Json`). Database drivers are isolated inside their storage provider packages. Even the cron parser is our own.
- **Zero reflection, Native AOT ready.** Job discovery, registration, invocation — including attribute reading — happen at compile time through source generators. The core builds clean under trimming and `PublishAot`.
- **Structured logging with no lock-in.** Guará logs through `Microsoft.Extensions.Logging` with structured properties (`JobId`, `Queue`, `Attempt`, `State`...). Plug any sink you like — JSON console, OpenTelemetry, Seq, ELK — the framework does not care.
- **Everything beyond the core is optional.** Dashboard, OpenTelemetry, cluster: separate packages. Install only what you use.

The name comes from the *lobo-guará* (maned wolf), a fast and resilient animal native to Brazil. Made in Brasil.

## Features

| Capability | Description |
|---|---|
| Fire-and-forget | Enqueue a job and return immediately |
| Delayed jobs | Run once after a given delay |
| Recurring jobs | Cron expressions with time zone and DST support |
| Continuations | Chain jobs: run B automatically when A finishes |
| Automatic retries | Exponential back-off, configurable per job, opt-out for non-idempotent work |
| Declarative attributes | `[GuaraFila]`, `[GuaraRetentativas]`, `[GuaraDesabilitarConcorrencia]`, `[GuaraTempoLimite]`... — behavior declared on the job itself |
| Real-time dashboard | Angular SPA fed by Server-Sent Events — state changes appear in about a second. **Optional**: separate package |
| Distributed processing | Multiple nodes, leader election, failover, distributed locks — coordinated through the storage itself |
| Pluggable storage | PostgreSQL, SQL Server, Redis, MongoDB, In-Memory — switch with one line |
| Observability | Structured logs, metrics (`System.Diagnostics.Metrics`), traces (`ActivitySource`), health checks, optional OpenTelemetry exporters |
| Secure by default | Dashboard denies anonymous access unless explicitly configured otherwise |

## Quick start (planned API)

Install the packages (names are final, publication pending). The core runs on its own — the dashboard is **optional**:

```bash
dotnet add package Guara
dotnet add package Guara.Storage.PostgreSql

# optional — only if you want the web dashboard
dotnet add package Guara.Dashboard
```

Configure everything with a fluent, ASP.NET Core-style API:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddGuara()
    .UsePostgreSqlStorage(builder.Configuration.GetConnectionString("guara")!)
    .AddGuaraServer()        // starts workers, scheduler, heartbeat
    .AddGuaraDashboard();    // OPTIONAL — remove it and Guará runs headless

var app = builder.Build();

app.MapGuaraDashboard();     // OPTIONAL — serves the SPA + API at /guara
app.Run();
```

It also runs in a plain Worker Service (no ASP.NET Core), for dedicated processing nodes:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddGuara()
    .UsePostgreSqlStorage(builder.Configuration.GetConnectionString("guara")!)
    .AddGuaraServer();

builder.Build().Run();
```

### Enqueuing jobs

Enqueue from anywhere through `IGuaraClient`.

> **A note on naming.** Guará is a Brazilian project, and its job-facing API is intentionally written in Portuguese — it is part of the project's identity ([ADR-0010](docs/adr/0010-api-do-usuario-em-portugues.md)). Everything else (types, DI extensions, options, routes) follows the standard .NET conventions in English. The table below is all you need:

| Method | Meaning |
|---|---|
| `EnfileirarAsync` | Enqueue (fire-and-forget) |
| `AgendarAsync` | Schedule (run once after a delay) |
| `AdicionarOuAtualizarRecorrenteAsync` | Add or update a recurring job |
| `ContinuarComAsync` | Continue with (continuation) |
| `ExcluirAsync` | Delete |

```csharp
public sealed class ReportService(IGuaraClient jobs)
{
    public async Task RequestAsync(int customerId, CancellationToken ct)
    {
        // Fire-and-forget: runs as soon as a worker is free
        await jobs.EnfileirarAsync(() => GenerateReportAsync(customerId), ct);

        // Delayed: run once, 24 hours from now
        await jobs.AgendarAsync(() => SendReminderAsync(customerId), TimeSpan.FromHours(24), ct);
    }

    public Task GenerateReportAsync(int customerId) { /* ... */ }
    public Task SendReminderAsync(int customerId) { /* ... */ }
}
```

### Recurring jobs (cron)

```csharp
// Every day at 03:00 UTC
await jobs.AdicionarOuAtualizarRecorrenteAsync(
    id: "nightly-cleanup",
    () => CleanupExpiredRecordsAsync(),
    cron: "0 3 * * *",
    TimeZoneInfo.Utc,
    ct);

// Every Monday at 08:00 São Paulo time (DST handled by the built-in cron parser)
await jobs.AdicionarOuAtualizarRecorrenteAsync(
    id: "weekly-digest",
    () => SendWeeklyDigestAsync(),
    cron: "0 8 * * MON",
    TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"),
    ct);
```

### Continuations and deletion

```csharp
// B runs automatically when A succeeds
var export = await jobs.EnfileirarAsync(() => ExportOrdersAsync(month), ct);
await jobs.ContinuarComAsync(export, () => NotifyExportFinishedAsync(month), ct);

// Cancel/delete a job that has not run yet
await jobs.ExcluirAsync(export, ct);
```

## Job attributes

Behavior is declared **on the job itself** — in Portuguese, like the rest of the user-facing API ([spec 036](spec/036-atributos-de-job.md)). Attributes are read at **compile time** by the source generator: zero reflection at runtime.

```csharp
public sealed class ReportJobs(IReportService service)
{
    [GuaraJob]                                          // discovery (compile-time generated)
    [GuaraFila("reports")]                              // dedicated queue
    [GuaraRetentativas(5)]                              // up to 5 retries with back-off
    [GuaraDesabilitarConcorrencia(Chave = "customer-{0}")] // never 2 executions for the same customer
    [GuaraTempoLimite(300)]                             // cancel after 5 minutes
    public Task GenerateAsync(int customerId, CancellationToken ct)
        => service.GenerateAsync(customerId, ct);

    [GuaraJob]
    [GuaraRetentativas(0)]                              // never retry: charging twice is worse than failing
    public Task ChargeCustomerAsync(int invoiceId, CancellationToken ct)
        => service.ChargeAsync(invoiceId, ct);

    [GuaraJob]
    [GuaraPularSeAnteriorEmExecucao]                    // recurring: skip if the previous run is still going
    public Task SyncCatalogAsync(CancellationToken ct)
        => service.SyncAsync(ct);
}
```

| Attribute | Effect |
|---|---|
| `[GuaraFila("name")]` | Sets the job's queue |
| `[GuaraRetentativas(n)]` | Per-job retry policy; `0` disables retries |
| `[GuaraDesabilitarConcorrencia]` | Mutual exclusion by key, across nodes (equivalent to Hangfire's `DisableConcurrentExecution`) |
| `[GuaraTempoLimite(seconds)]` | Maximum execution time; exceeded → cooperative cancellation |
| `[GuaraPularSeAnteriorEmExecucao]` | Recurring: skip the occurrence if the previous one is still running |

## How it works

Every component reacts to an event and emits the next one — no component ever calls another directly:

```
JobCreated -> Scheduler -> JobScheduled -> Dispatcher -> WorkerRequested
           -> Worker -> ExecutorStarted -> Executor -> JobCompleted | JobFailed
```

Inside the executor, each job runs through a middleware pipeline, modeled after ASP.NET Core:

```
Validation -> Authorization -> Serialization -> Custom middleware
           -> Metrics -> Logging -> Retry -> Execution -> Success -> Notifications
```

Each stage is an `IJobMiddleware`; the `Custom` slot is the user extension point. The full architecture — dependency rules, naming conventions, execution flows, and the ADRs behind every decision — is documented in [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) (Portuguese).

## Storage providers

`Guara.Storage` defines the contracts; each provider implements them using the best primitives of its backend. All providers must pass the same **conformance test kit** (atomic acquisition under concurrency, lease/visibility, idempotency, TTL locks).

| Provider | Atomic dequeue | Distributed lock | Real-time push |
|---|---|---|---|
| PostgreSQL | `FOR UPDATE SKIP LOCKED` | Advisory locks | `LISTEN/NOTIFY` |
| SQL Server | `READPAST + UPDLOCK` | `sp_getapplock` | Polling |
| Redis | Lua scripts | `SET NX PX` + TTL | Keyspace notifications |
| MongoDB | `findAndModify` | TTL collection | Change streams |
| In-Memory | Lock-free structures | Process-local | Immediate |

Switching provider is a one-line change:

```csharp
builder.Services.AddGuara().UseMemoryStorage();                    // dev/tests
builder.Services.AddGuara().UsePostgreSqlStorage(connectionString); // production
```

## Dashboard (optional)

The dashboard is **not required**: Guará's core runs on its own and the panel is a separate package (`Guara.Dashboard`) — install it only if you want the web UI. It is an Angular SPA served as embedded static assets — no separate deployment, no Node.js at runtime. It consumes only the versioned HTTP API (`/guara/api/v1`) and updates in real time through Server-Sent Events. Authentication integrates with the host's ASP.NET Core schemes (cookie, JWT, OIDC), and every action is gated by fine-grained permissions (`guara:view`, `guara:trigger`, `guara:retry`, `guara:delete`, `guara:view-payload`). Anonymous access is denied by default.

## Configuration

All configuration follows the .NET Options pattern under the `Guara` section (validated at startup — invalid configuration fails at boot, not in production):

```json
{
  "Guara": {
    "Storage": { "Provider": "PostgreSql", "ConnectionString": "..." },
    "Dispatcher": { "PollingInterval": "00:00:05", "Queues": [ "high", "default" ] },
    "Worker": { "MaxConcurrency": 8, "ShutdownDrainTimeout": "00:00:30" },
    "Server": { "Retention": { "Succeeded": "1.00:00:00", "Failed": "7.00:00:00" } },
    "Dashboard": { "BasePath": "/guara", "RequireAuthorization": true }
  }
}
```

## Observability

- **Logs**: structured, via `Microsoft.Extensions.Logging` — properties like `JobId`, `Queue`, `JobType`, `Attempt`, `DurationMs` on every record. The sample host writes JSON to stdout using the built-in console formatter; bring your own sink if you prefer.
- **Metrics**: `System.Diagnostics.Metrics` (`guara.jobs.processed`, `guara.jobs.failed`, `guara.job.duration`, `guara.queue.length`).
- **Traces**: one `Activity` span per job execution (`ActivitySource("Guara")`).
- **Health checks**: storage reachability, server liveness, queue thresholds.
- **OpenTelemetry**: the optional `Guara.OpenTelemetry` package registers Guará's sources into your existing OTel pipeline. The core never forces an exporter on you.

## Roadmap

| Milestone | Status |
|---|---|
| Architecture documentation and ADRs | Done |
| Full specification (36 specs, one per component/feature) | Done |
| Foundation: `Guara.Abstractions` + `Guara.Core` (pipeline, state machine, events) | Done |
| Serialization (`Guara.Serialization` — source-gen, allowlist) | Done |
| Storage contracts + In-Memory provider + conformance kit | Done |
| Engines: Scheduler (built-in cron), Dispatcher, Worker, Executor | In progress |
| Hosting, Server, PostgreSQL provider | Planned |
| Dashboard (API + Angular SPA, real-time) | Planned |
| Remaining providers, cluster, CLI, analyzers, job attributes | Planned |
| First NuGet release | Planned |

## Documentation

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — architectural philosophy, dependency rules, package structure, execution flows, performance principles (Portuguese)
- [`docs/adr/`](docs/adr/) — Architecture Decision Records
- [`spec/`](spec/) — the full specification, one document per component, with acceptance criteria
- [`Infra/`](Infra/) — reference Docker deployment (PostgreSQL + reverse proxy)

## Contributing

Guará is being built specification-first: every component has an approved spec with acceptance criteria before any code is written. If you want to contribute, start by reading [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) and the spec of the component you are interested in. Contribution guidelines and issue templates will be published before the first release.

## License

Guará's core — everything documented in this repository — is licensed under **LGPL-3.0**, which allows free use in commercial and proprietary applications via the standard NuGet packages. A small set of advanced add-ons (such as batch orchestration) is planned as separately licensed commercial packages that help fund the project's development; the core will always remain free and open source.
