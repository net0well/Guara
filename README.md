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
  English | <a href="README.pt-BR.md">Português (Brasil)</a>
</p>

---

> **Project status.** Guará is under active development and has not been published to NuGet yet. The public API shown below reflects the approved specifications (see [`spec/`](spec/)) and may change until the first stable release. Star and watch the repository to follow the progress.

## What is Guará

Guará is an open source **job scheduler for .NET**, in the same problem space as Hangfire: fire-and-forget jobs, delayed execution, cron-based recurring jobs, automatic retries, distributed processing across nodes, and a real-time dashboard to observe and operate everything.

What makes it different is the way it is built:

- **Component-based architecture.** There are no traditional layers. Every responsibility — scheduling, dispatching, executing, storing, observing — is an independent component with its own contracts, package, tests, and lifecycle. Components communicate only through interfaces and events, never by referencing each other's implementations.
- **The storage is the queue.** No message broker required. Jobs live in your database, and atomic acquisition with leases (visibility timeout) guarantees each job is processed exactly by one worker, even across many nodes.
- **Zero third-party dependencies in the core.** The runtime depends only on the .NET platform (`Microsoft.Extensions.*`, `System.Text.Json`). Database drivers are isolated inside their storage provider packages. Even the cron parser is our own.
- **Zero reflection, Native AOT ready.** Job discovery, registration, and invocation are generated at compile time by source generators. The core builds clean under trimming and `PublishAot`.
- **Structured logging with no lock-in.** Guará logs through `Microsoft.Extensions.Logging` with structured properties (`JobId`, `Queue`, `Attempt`, `State`...). Plug any sink you like — JSON console, OpenTelemetry, Seq, ELK — the framework does not care.

The name comes from the *lobo-guará* (maned wolf), a fast and resilient animal native to Brazil. Made in Brasil.

## Features

| Capability | Description |
|---|---|
| Fire-and-forget | Enqueue a job and return immediately |
| Delayed jobs | Run once after a given delay |
| Recurring jobs | Cron expressions with time zone and DST support |
| Continuations | Chain jobs: run B automatically when A finishes |
| Automatic retries | Exponential back-off, configurable per job, opt-out for non-idempotent work |
| Real-time dashboard | Angular SPA fed by Server-Sent Events — state changes appear in about a second |
| Distributed processing | Multiple nodes, leader election, failover, distributed locks — coordinated through the storage itself |
| Pluggable storage | PostgreSQL, SQL Server, Redis, MongoDB, In-Memory — switch with one line |
| Observability | Structured logs, metrics (`System.Diagnostics.Metrics`), traces (`ActivitySource`), health checks, optional OpenTelemetry exporters |
| Security by default | Dashboard denies anonymous access unless explicitly configured otherwise |

## Quick start (planned API)

Install the packages (names are final, publication pending):

```bash
dotnet add package Guara
dotnet add package Guara.Storage.PostgreSql
```

Configure everything with a fluent, ASP.NET Core-style API:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddGuara()
    .UsePostgreSqlStorage(builder.Configuration.GetConnectionString("guara")!)
    .AddGuaraServer()        // starts workers, scheduler, heartbeat
    .AddGuaraDashboard();    // real-time dashboard

var app = builder.Build();

app.MapGuaraDashboard();     // serves the SPA + API at /guara
app.Run();
```

Enqueue jobs from anywhere through `IGuaraClient`:

```csharp
public sealed class ReportService(IGuaraClient jobs)
{
    public async Task RequestAsync(int customerId, CancellationToken ct)
    {
        // Fire-and-forget
        await jobs.EnqueueAsync(() => GenerateReportAsync(customerId), ct);

        // Delayed: run once, 24 hours from now
        await jobs.ScheduleAsync(() => SendReminderAsync(customerId), TimeSpan.FromHours(24), ct);
    }

    public Task GenerateReportAsync(int customerId) { /* ... */ }
    public Task SendReminderAsync(int customerId) { /* ... */ }
}
```

Recurring jobs use plain cron expressions:

```csharp
await jobs.AddOrUpdateRecurringAsync(
    id: "nightly-cleanup",
    () => CleanupExpiredRecordsAsync(),
    cron: "0 3 * * *",             // every day at 03:00
    TimeZoneInfo.Utc,
    ct);
```

Chain work with continuations:

```csharp
var export = await jobs.EnqueueAsync(() => ExportOrdersAsync(month), ct);

// Runs automatically when the export succeeds
await jobs.ContinueWithAsync(export, () => NotifyExportFinishedAsync(month), ct);
```

Retries are automatic (3 attempts with exponential back-off by default). Jobs with irreversible side effects can opt out:

```csharp
[GuaraJob(MaxAttempts = 0)]   // never retry: sending twice would be worse than failing
public async Task ChargeCustomerAsync(int invoiceId, CancellationToken ct) { /* ... */ }
```

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

Each stage is an `IJobMiddleware`; the `Custom` slot is the user extension point. The full architecture — dependency rules, naming conventions, execution flows, and the ADRs behind every decision — is documented in [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Storage providers

`Guara.Storage` defines the contracts; each provider implements them using the best primitives of its backend. All providers must pass the same conformance test kit.

| Provider | Atomic dequeue | Distributed lock | Real-time push |
|---|---|---|---|
| PostgreSQL | `FOR UPDATE SKIP LOCKED` | Advisory locks | `LISTEN/NOTIFY` |
| SQL Server | `READPAST + UPDLOCK` | `sp_getapplock` | Polling |
| Redis | Lua scripts | `SET NX PX` + TTL | Keyspace notifications |
| MongoDB | `findAndModify` | TTL collection | Change streams |
| In-Memory | Lock-free structures | Process-local | Immediate |

Switching provider is a one-line change. Capabilities that differ between backends (rich dashboard queries, server-side push) are declared explicitly and degrade transparently.

## Dashboard

The dashboard is an Angular SPA served as embedded static assets — no separate deployment, no Node.js at runtime. It consumes only the versioned HTTP API (`/guara/api/v1`) and updates in real time through Server-Sent Events. Authentication integrates with the host's ASP.NET Core schemes (cookie, JWT, OIDC), and every action is gated by fine-grained permissions (`guara:view`, `guara:trigger`, `guara:retry`, `guara:delete`, `guara:view-payload`). Anonymous access is denied by default.

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
| Full specification (35 specs, one per component) | Done |
| Foundation: `Guara.Abstractions` + `Guara.Core` (pipeline, state machine, events) | Done |
| Serialization and storage contracts | In progress |
| Engines: Scheduler, Dispatcher, Worker, Executor | Planned |
| Hosting, Server, PostgreSQL provider | Planned |
| Dashboard (API + Angular SPA, real-time) | Planned |
| Remaining providers, cluster, CLI, analyzers | Planned |
| First NuGet release | Planned |

## Documentation

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — architectural philosophy, dependency rules, package structure, execution flows, performance principles
- [`docs/adr/`](docs/adr/) — Architecture Decision Records
- [`spec/`](spec/) — the full specification, one document per component, with acceptance criteria
- [`Infra/`](Infra/) — reference Docker deployment (PostgreSQL + reverse proxy)

## Contributing

Guará is being built specification-first: every component has an approved spec with acceptance criteria before any code is written. If you want to contribute, start by reading [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) and the spec of the component you are interested in. Contribution guidelines and issue templates will be published before the first release.

## License

Guará's core — everything documented in this repository — is licensed under **LGPL-3.0**, which allows free use in commercial and proprietary applications via the standard NuGet packages. A small set of advanced add-ons (such as batch orchestration) is planned as separately licensed commercial packages that help fund the project's development; the core will always remain free and open source.
