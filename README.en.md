<p align="center">
  <img src="assets/logo-escrita.png" alt="Guará — Job Scheduler" width="440">
</p>

<p align="center">
  <strong>Background jobs and scheduling for modern .NET — component-based, storage-agnostic, AOT-ready.</strong>
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/Guara.Hosting"><img src="https://img.shields.io/nuget/vpre/Guara.Hosting?label=NuGet&color=004880" alt="NuGet"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache--2.0-blue" alt="License: Apache-2.0"></a>
  <img src="https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4" alt=".NET 8.0 | 10.0">
  <img src="https://img.shields.io/badge/status-under%20active%20development-orange" alt="Status: under active development">
</p>

<p align="center">
  <a href="README.md">Português (Brasil)</a> | English
</p>

---

> **Project status.** Guará is under active development, published on NuGet as a **preview** (`0.1.0-preview.3`). The runtime, the full dashboard and four production storages — PostgreSQL, SQL Server, MySQL and MongoDB — are implemented and covered by tests. Being a prerelease, the public API may still change until 1.0 — anything not there yet is marked _planned_ throughout this document and in the [roadmap](#roadmap). Star and watch the repository to follow the progress.

## Installation

Prereleases require the `--prerelease` flag; without it NuGet ignores the version:

```bash
dotnet add package Guara.Hosting --prerelease              # entry point: AddGuara()
dotnet add package Guara.Server --prerelease               # runs jobs in this process
dotnet add package Guara.Storage.PostgreSql --prerelease   # storage (pick one)
dotnet add package Guara.Dashboard --prerelease            # optional: web dashboard
```

Storage is a single choice: `Guara.Storage.PostgreSql`, `Guara.Storage.SqlServer`, `Guara.Storage.MySql` or `Guara.Storage.Mongo` in production; `Guara.Storage.Memory` for development and tests. They all pass the same conformance kit, and switching between them is one line.

Running on several nodes and already have Redis? `Guara.Redis` is optional and carries the work signal between them — [details below](#what-about-redis).

## Packages

Install only what you use — the core runs on its own and everything else is optional. The state column separates what is already published from what is still to come.

| Package | Purpose | State |
|---|---|---|
| `Guara.Hosting` | Entry point: `AddGuara()` and the fluent builder | ✅ published |
| `Guara.Server` | Lifecycle: workers, scheduler, heartbeat, maintenance | ✅ published |
| `Guara.Scheduler` | Own cron, recurring jobs, calendars, `IGuaraClient` | ✅ published |
| `Guara.Storage.PostgreSql` | PostgreSQL storage | ✅ published |
| `Guara.Storage.SqlServer` | SQL Server 2016+ storage | ✅ published |
| `Guara.Storage.MySql` | MySQL 8+ storage | ✅ published |
| `Guara.Storage.Mongo` | MongoDB storage | ✅ published |
| `Guara.Storage.Memory` | In-memory storage — dev, tests and demos | ✅ published |
| `Guara.Dashboard` | Web dashboard (API + embedded Angular SPA, real-time) | ✅ published |
| `Guara.Authorization` | Per-action dashboard permissions | ✅ published |
| `Guara.Diagnostics` | Structured logs, metrics and traces | ✅ published |
| `Guara.SourceGenerators` | Reflection-free job registration and invocation | ✅ published |
| `Guara.Analyzers` | Roslyn analyzers enforcing the dependency rules | ✅ published |
| `Guara.Abstractions` / `Guara.Storage` | Contracts — for provider and extension authors | ✅ published |
| `Guara.Redis` | Accelerator: carries the work signal across nodes over pub/sub | ✅ published |
| `Guara.Authentication` | Authentication schemes (JWT, OIDC, cookie) | 🕓 planned |
| `Guara.Cluster` | Leader election with renewed ownership, for work that does not split across nodes | 🚧 in the repo, ships next preview |
| `Guara.OpenTelemetry` | OpenTelemetry exporters | 🕓 planned |
| `Guara.Cli` | Command-line tool (`dotnet tool`) | 🕓 planned |
| `Guara.Pro.Batches` | Commercial: job groups with completion callbacks | 🕓 planned |

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
| Recurring jobs | Cron expressions or intervals, with a Quartz-style **fluent builder** (`ComId`, `IniciaEm`, `ComCalendario`...) |
| Calendars | Excluded dates and windows (holidays, weekends) reusable across recurring jobs — manageable from code or from the dashboard |
| Native time zones | IANA (`America/Sao_Paulo`) and Windows ids accepted on both OSes — no third-party packages |
| Continuations | Chain jobs: run B automatically when A finishes |
| Automatic retries | Exponential back-off, configurable per job, opt-out for non-idempotent work |
| Declarative attributes | `[GuaraFila]`, `[GuaraRetentativas]`, `[GuaraDesabilitarConcorrencia]`, `[GuaraTempoLimite]`... — behavior declared on the job itself |
| Real-time dashboard | Angular SPA fed by Server-Sent Events — state changes appear in about a second. **Optional**: separate package |
| Operable dashboard | Search by text/type/queue/state/period, live throughput and latency charts (p50/p95), recurring management (pause, resume, trigger, edit schedule), calendar editing on a monthly grid, and bulk actions |
| Dashboard authentication | Fluent rules (authenticated users, roles, claims, internal IPs, combinable), custom rules with `HttpContext`, fixed credentials and a **branded login page** |
| Granular permissions | Every dashboard action requires its own grant (`guara:view`, `guara:retry`, `guara:trigger`, `guara:delete`, `guara:calendars`, `guara:view-payload`), on top of ASP.NET Core policies |
| Pluggable storage | Shared contracts with a conformance kit every provider inherits. Today: PostgreSQL, SQL Server, MySQL, MongoDB and In-Memory — switch with one line |
| Observability | Structured logs, metrics (`System.Diagnostics.Metrics`), traces (`ActivitySource`) |
| Secure by default | Dashboard denies anonymous access unless explicitly configured otherwise; whatever was not granted is denied |
| Multi-node | Distributed execution via lease ownership; work that does not split (recurring, maintenance) runs under leader election with renewed ownership, and the dashboard shows which node holds each role |
| _Planned_ | OpenTelemetry exporters, CLI and authentication as its own package |

## Quick start

With the packages installed (see [Installation](#installation)), configure everything with a fluent, ASP.NET Core-style API:

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

Mark the method with `[GuaraJob]` and a source generator produces the `{Type}Guara` descriptor factory. No lambdas, no reflection: queue and arguments are resolved at compile time, and a wrong signature is a build error rather than a production failure.

```csharp
public sealed class ReportService(IGuaraClient jobs)
{
    public async Task RequestAsync(int customerId, CancellationToken ct)
    {
        // Fire-and-forget: runs as soon as a worker is free
        await jobs.EnfileirarAsync(ReportServiceGuara.GenerateReport(customerId), ct);

        // Delayed: run once, 24 hours from now
        await jobs.AgendarAsync(
            ReportServiceGuara.SendReminder(customerId), TimeSpan.FromHours(24), ct);
    }

    [GuaraJob]
    public Task GenerateReport(int customerId) { /* ... */ }

    [GuaraJob]
    public Task SendReminder(int customerId) { /* ... */ }
}
```

Registration is generated too: `builder.Services.AddGuara().AddGuaraJobs()` wires every marked job, with no runtime assembly scanning.

### Enqueuing together with your data (transactional)

The classic failure mode: you save the order, enqueue the confirmation email, and the order transaction rolls back. A worker then processes a job for an order that never existed. Or the reverse — the commit succeeds, the enqueue fails, and the email never goes out.

Hand over your transaction and the two become one operation:

```csharp
await using var tx = await db.Database.BeginTransactionAsync(ct);

db.Orders.Add(order);
await db.SaveChangesAsync(ct);

await jobs.EnfileirarAsync(
    () => SendConfirmationAsync(order.Id),
    new RelationalTransaction(db.Database.GetDbTransaction()),
    ct);

await tx.CommitAsync(ct);   // either both happen, or neither
```

**You** own the transaction; Guará only writes inside it and never commits or rolls back. This requires Guará to live in the **same database** as your application — which is exactly what the schema and prefix isolation is for.

Two caveats worth knowing before you reach for it:

- **The id comes back before the commit.** Recording it outside the transaction (log, HTTP response, another connection) is on you: a rollback leaves that id pointing at nothing.
- **This path does not signal the queue.** Guará cannot see your commit, so waking the dispatcher now would send it looking for a job that is not visible yet. The job is picked up on the next polling cycle — atomicity at the cost of some latency.

Available on **PostgreSQL, SQL Server and MySQL**. MongoDB and in-memory declare `SupportsTransactions: false` and refuse the call with an explicit message: multi-document transactions in MongoDB require a replica set, and a standalone server does not offer one. See [ADR-0014](docs/adr/0014-enfileiramento-transacional.md).

### Recurring jobs (fluent builder, Quartz-style)

Recurring jobs are configured through a **fluent builder** ([spec 038](spec/038-agendamento-fluente.md)) — identity, schedule, validity window, description, and calendar in one place (`ComId` = with id, `IniciaEm` = start at, `ACada` = every, `ComCalendario` = with calendar):

```csharp
await jobs.AdicionarOuAtualizarRecorrenteAsync(job => job
    .ComId("nightly-cleanup")
    .Executa(MaintenanceServiceGuara.CleanupExpiredRecords())
    .ComCron("0 3 * * *")                                          // every day at 03:00
    .NoFusoHorario("America/Sao_Paulo")                            // accepts IANA or Windows ids
    .IniciaEm(GuaraDatas.SegundoExato(DateTimeOffset.UtcNow.AddSeconds(7)))
    .ComDescricao("Removes expired records")
    .ComCalendario("holidays"),                                    // skips excluded dates
    ct);

// Interval-based schedule (no cron), with a daily window
await jobs.AdicionarOuAtualizarRecorrenteAsync(job => job
    .ComId("price-sync")
    .Executa(PriceServiceGuara.SyncPrices())
    .ACada(TimeSpan.FromSeconds(10))
    .EntreHorarios(new TimeOnly(8, 0), new TimeOnly(18, 0)),
    ct);
```

`GuaraDatas` is the fire-date builder (Quartz's `DateBuilder` equivalent): `SegundoExato` (even second), `HojeAs(3, 0)` (today at), `AmanhaAs(8, 0)` (tomorrow at), `ProximoDiaUtil()` (next business day)... Windows↔Linux time-zone conversion (IANA↔Windows) is **built-in and automatic** — no third-party packages.

### Calendars (holidays and excluded windows)

Calendars are persisted and reusable across recurring jobs; changing a calendar **automatically recomputes** the next fire time of every job using it:

```csharp
await jobs.AdicionarOuAtualizarCalendarioAsync("holidays", cal => cal
    .ExcluirData(new DateOnly(2026, 12, 25))
    .ExcluirData(new DateOnly(2027, 1, 1))
    .ExcluirDiasDaSemana(DayOfWeek.Sunday),
    ct);
```

Calendars can also be created and maintained **through the dashboard UI** — a lightweight month view for adding holidays and excluding dates — with the same effect: recurring jobs using them are recomputed automatically, whether the change comes from code or from the panel.

### Continuations and deletion

```csharp
// B runs automatically when A succeeds
var export = await jobs.EnfileirarAsync(OrderServiceGuara.ExportOrders(month), ct);
await jobs.ContinuarComAsync(export, OrderServiceGuara.NotifyExportFinished(month), ct);

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

### Architecture rules that break the build

The dependency rules do not live in documentation alone: `Guara.Analyzers` turns them into compiler errors, and every Guará package builds with it enabled.

| Rule | What it prevents |
|---|---|
| `GUARA0001` | Inverted dependency — a component referencing another one from a higher layer |
| `GUARA0002` | An execution engine reaching a concrete provider instead of the contract |

## Storage providers

`Guara.Storage` defines the contracts; each provider implements them using the best primitives of its backend. All providers must pass the same **conformance test kit** (atomic acquisition under concurrency, lease/visibility, idempotency, TTL locks).

| Provider | Atomic dequeue | Distributed lock | State |
|---|---|---|---|
| PostgreSQL | `FOR UPDATE SKIP LOCKED` | Table with expiry and owner | ✅ published, conformance green |
| SQL Server 2016+ | `READPAST + UPDLOCK` with `OUTPUT` | Table with expiry and owner | ✅ published, conformance green |
| MySQL 8+ | `FOR UPDATE SKIP LOCKED` | Table with expiry and owner | ✅ published, conformance green |
| MongoDB | `findAndModify` | Document with expiry and owner | ✅ published, conformance green |
| In-Memory | Mutual exclusion over the dictionary | Process-local | ✅ published, conformance green |

The three relational ones accept **enqueuing inside your transaction** — see [above](#enqueuing-together-with-your-data-transactional). MongoDB and in-memory declare `SupportsTransactions: false` and refuse the call.

Every provider isolates what it writes from the rest of the database. PostgreSQL and SQL Server use a dedicated **schema** (`Schema`, default `guara`); in MySQL schema and database are the same thing, so isolation is by **table prefix** (`TablePrefix`, default `guara_`); in MongoDB, by **collection prefix** (`CollectionPrefix`, default `guara_`).

Switching provider is a one-line change:

```csharp
builder.Services.AddGuara().UseMemoryStorage();                    // dev/tests
builder.Services.AddGuara().UsePostgreSqlStorage(connectionString); // production
builder.Services.AddGuara().UseSqlServerStorage(connectionString);  // production
builder.Services.AddGuara().UseMySqlStorage(connectionString);      // production
builder.Services.AddGuara().UseMongoStorage(connectionString);      // production
```

### What about Redis?

Redis is **not shipping as a storage**, and that is a decision, not a gap. A scheduler cannot lose jobs: RDB loses the window since the last snapshot, and AOF `everysec` loses up to a second. On top of that, the dashboard needs filtering by state, queue, type, text and period, with pagination, counts and latency percentiles — in Redis that would mean half a dozen hand-maintained secondary indexes with no transaction keeping them consistent with each other.

Redis ships as an **accelerator** instead (`Guara.Redis`), doing what it is genuinely good at: delivering a notification to every node in milliseconds. Enqueuing on one node wakes every other node's dispatcher right away, without shortening the polling interval. The durable truth stays in the storage provider.

```csharp
builder.Services.AddGuara()
    .UsePostgreSqlStorage(connectionString)  // the durable truth
    .UseRedis("localhost:6379");             // the signal, across nodes
```

Nothing in it is durable, and nothing needs to be: the signal is best-effort and the polling cycle is the floor. With Redis down, the node that enqueued still wakes on its own and the others fall back to the configured interval — no job is lost. If your application already registers an `IConnectionMultiplexer`, Guará uses it and the connection string becomes unnecessary.

**Distributed locks and a read cache were deliberately left out**: all four production storages already do distributed locking on their own, and a dashboard cache depends on an invalidation policy that does not exist yet — shipping it now would mean a dashboard showing the past. See [ADR-0013](docs/adr/0013-redis-como-acelerador.md).

## Dashboard (optional)

The dashboard is **not required**: Guará's core runs on its own and the panel is a separate package (`Guara.Dashboard`) — install it only if you want the web UI. It is an Angular SPA served as embedded static assets — no separate deployment, no Node.js at runtime. It consumes only the versioned HTTP API (`/guara/api/v1`) and updates in real time through Server-Sent Events. **Anonymous access is denied by default.**

### Dashboard authentication

Protecting the dashboard does not require hand-writing a filter ([spec 037](spec/037-dashboard-autenticacao.md)) — common rules are fluent and **combinable** (all must pass; use `QualquerUma(...)` for "any of"):

```csharp
builder.Services
    .AddGuara()
    .AddGuaraDashboard(dash => dash
        .UseGuaraAuthentication(auth => auth
            .PermitirApenasLogados()                 // require authenticated user
            .ExigirPapel("Admin")                    // admins only (role)
            .ExigirClaim("department", "it")         // require a claim
            .PermitirApenasIpsInternos()));          // internal networks/loopback only
```

For simple scenarios (internal network, staging) there are **fixed credentials** — with a branded login page (Guará logo, light/dark theme) and brute-force protection:

```csharp
.UseGuaraAuthentication(auth => auth
    .ComLoginFixo(
        usuario: "guara_admin",
        senha: builder.Configuration["Guara:Dashboard:Password"]!)  // env/secret, never a literal
    .PermitirApenasIpsInternos());
```

When the built-in rules are not enough, implement `IDashboardAccessRule` — the (cleaner) equivalent of Hangfire's `IDashboardAuthorizationFilter`, with full `HttpContext` access:

```csharp
public sealed class BusinessHoursOnly : IDashboardAccessRule
{
    public ValueTask<bool> AutorizarAsync(DashboardContext contexto, CancellationToken ct)
    {
        var http = contexto.HttpContext;             // = Hangfire's GetHttpContext()
        var hour = TimeProvider.System.GetLocalNow().Hour;
        return ValueTask.FromResult(
            contexto.User.Identity?.IsAuthenticated == true && hour is >= 8 and < 18);
    }
}

// register: .UseGuaraAuthentication(auth => auth.ComRegra<BusinessHoursOnly>())
```

### Permissions inside the panel

The rules above decide **who gets in**. What each person can **do** once inside belongs to `Guara.Authorization`, and is denied by default:

```csharp
builder.Services
    .AddGuara()
    .AddGuaraAuthorization(auth => auth
        .Require(GuaraActions.Delete, "SupportOnly")   // an ASP.NET Core policy
        .AllowAll("DashboardAdministrator"));          // or everything at once
```

Recognised actions: `guara:view`, `guara:view-payload`, `guara:retry`, `guara:trigger`, `guara:delete` and `guara:calendars`. Without `AddGuaraAuthorization()`, the panel stays all-or-nothing — whoever passes the access rules operates everything. With it, each route requires its own grant, coming from an ASP.NET Core policy, an administrator role, or a `guara:permission` claim.

### Operating from the panel

Beyond observing, the panel **operates**: search by text, type, queue, state and period; live charts of throughput and p50/p95 latency; pause, resume, trigger and edit the schedule of recurring jobs; create and edit calendars on a clickable monthly grid; and retry or delete jobs in bulk, with the outcome reported item by item.

## Performance

Guará makes claims about performance — zero reflection, `ValueTask` on the hot path, low allocation — and those claims have numbers in [`benchmarks/`](benchmarks/README.md). A few, on a Xeon E5-2670 v3:

**End-to-end execution throughput**, with worker and dispatcher running against a real database, 10,000 jobs and 64 workers:

| Provider | Jobs/s | p50 latency | Allocated/job |
|---|---:|---:|---:|
| PostgreSQL | **3,821** | 6.1 ms | 16 KB |
| SQL Server | **1,855** | 13.1 ms | 60 KB |
| MySQL | **1,738** | 16.6 ms | 29 KB |

Latency is fire-and-forget on an empty queue, from enqueue to execution start, with the polling cycle set to **60 seconds** — meaning it is the [queue signal](docs/adr/0012-wakeup-por-sinal-de-fila.md) at work, not polling.

These numbers came from three measurement-driven fixes, each attacking what the previous one exposed: [materialized eligibility](docs/adr/0015-elegibilidade-como-instante-indexavel.md), automatic command preparation and [batch acquisition](docs/adr/0016-aquisicao-em-lote.md). The starting point was 151 jobs/s on PostgreSQL and 87 on MySQL.

What the numbers do **not** favour is recorded in the same place — among other things, the in-house cron parser is slower than `Cronos`, which is what Hangfire internalizes.

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

`Dispatcher.PollingInterval` is the **ceiling** of the wait, not the cadence: enqueuing signals the queue and the dispatcher wakes right away. The interval is the guarantee for what becomes eligible on its own — a retry that came due, a lease abandoned by a node that crashed. Raising it cuts idle load against the database without costing latency.

## Observability

- **Logs**: structured, via `Microsoft.Extensions.Logging` — properties like `JobId`, `Queue`, `JobType`, `Attempt`, `DurationMs` on every record. The sample host writes JSON to stdout using the built-in console formatter; bring your own sink if you prefer.
- **Metrics**: `System.Diagnostics.Metrics` (`guara.jobs.processed`, `guara.jobs.failed`, `guara.job.duration`, `guara.queue.length`).
- **Traces**: one `Activity` span per job execution (`ActivitySource("Guara")`).
- **Time series**: the dashboard API aggregates throughput, success/failure and p50/p95 latency per bucket, over 1h, 24h and 7d windows.
- **OpenTelemetry** _(planned)_: an optional package will register Guará's sources into your existing OTel pipeline. The core never forces an exporter on you — the sources already exist today and can be consumed directly.

## Roadmap

| Milestone | Status |
|---|---|
| Architecture documentation and ADRs | ✅ Done |
| Full specification (40 specs, one per component/feature) | ✅ Done |
| Foundation: `Guara.Abstractions` + `Guara.Core` (pipeline, state machine, events) | ✅ Done |
| Storage contracts + In-Memory provider + conformance kit | ✅ Done |
| Engines: Scheduler (built-in cron), Dispatcher, Worker, Executor | ✅ Done |
| Hosting, Server and PostgreSQL provider | ✅ Done |
| Continuations, job attributes and source generators | ✅ Done |
| Fluent scheduling: builder, `GuaraDatas`, calendars, native time zones | ✅ Done |
| Dashboard: v1 API with SSE + Angular SPA (overview, jobs, recurring, servers) | ✅ Done |
| Dashboard authentication: fluent rules, fixed login, login page | ✅ Done |
| Operable panel: search, live charts, calendars, bulk actions | ✅ Done |
| `Guara.Authorization`: per-action permissions, denied by default | ✅ Done |
| Apache-2.0 licensing, assembly signing and repository governance | ✅ Done |
| Packaging: version from tag, SourceLink, symbols, package metadata | ✅ Done |
| Public API freeze (`PublicApiAnalyzers`) | ✅ Done |
| CI/CD: multi-TFM build, containerised conformance, publish on tag | ✅ Done |
| **First NuGet release (`0.1.0-preview.1`)** | ✅ Done |
| SQL Server, MySQL and MongoDB providers (same conformance kit, 100% green) | ✅ Done |
| `Guara.Analyzers`: `GUARA0001` and `GUARA0002` enabled across the repository | ✅ Done |
| **`0.1.0-preview.2` release: four production storages and the analyzers** | ✅ Done |
| Queue-signal wakeup: the dispatcher wakes on enqueue, without shortening the interval | ✅ Done |
| `Guara.Redis`: the queue signal over pub/sub, waking every node's dispatcher | ✅ Done |
| **`0.1.0-preview.3` release: queue-signal wakeup and the Redis accelerator** | ✅ Done |
| Enqueuing inside the caller's transaction (PostgreSQL, SQL Server, MySQL) | ✅ Done |
| Benchmarks: cron, enqueue and in-memory storage | ✅ Done |
| Batch acquisition and indexed eligibility: 25× on PostgreSQL, 20× on MySQL, 10.6× on SQL Server | ✅ Done |
| `Guara.Cluster`: leader election with renewed ownership, roles visible in the dashboard | ✅ Done |
| `Guara.Serialization` removed: published in the previews with no consumer whatsoever ([ADR-0019](docs/adr/0019-guara-serialization-sai-do-catalogo.md)) | ✅ Done |
| User documentation, sample project and Hangfire migration guide | 🕓 Planned |
| **1.0** — frozen API, transactions settled in the contract, real-world burn-in | 🕓 Planned |
| `Guara.OpenTelemetry` (1.1) · `Guara.Cli` and `Guara.Authentication` (1.2) | 🕓 Planned |

## Semantics and guarantees

Guará documents its guarantees precisely in [`docs/semantics.md`](docs/semantics.md) (Portuguese) — read it before designing your jobs. The key points:

- **At-least-once delivery**: a job may run more than once under failure (worker dies after doing the work, before persisting the final state). Idempotent jobs are the ideal case; for irreversible side effects use `[GuaraRetentativas(0)]` plus idempotency at the destination; for mutual exclusion, `[GuaraDesabilitarConcorrencia]`.
- **Cooperative cancellation**: a side effect that already happened is never rolled back; a shutdown mid-execution leaves the state untouched and the lease guarantees reprocessing.
- **Recurring jobs**: occurrences overlap by default (like Quartz/Hangfire); a misfire runs **one** catch-up occurrence on restart; resuming a paused job does not backfill.
- **Strict queue priority**: list order is law — size your queues/workers to avoid starvation.
- **~FIFO start order per queue**, no completion-order guarantee; fire precision is bounded by polling/push (not a real-time system).

## Documentation

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — architectural philosophy, dependency rules, package structure, execution flows, performance principles (Portuguese)
- [`docs/semantics.md`](docs/semantics.md) — semantic guarantees (delivery, ordering, retries, cancellation, recurring jobs)
- [`docs/adr/`](docs/adr/) — Architecture Decision Records
- [`spec/`](spec/) — the full specification, one document per component, with acceptance criteria
- [`Infra/`](Infra/) — reference Docker deployment (PostgreSQL + reverse proxy)

## Contributing

Guará is being built specification-first: every component has an approved spec with acceptance criteria before any code is written. Start with [`CONTRIBUTING.md`](CONTRIBUTING.md) — it covers running locally, the three architectural laws, and how a change reaches `main`. Then read [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) and the spec of the component you care about. Contributor-facing docs are written in Portuguese.

This project adopts the [Contributor Covenant](CODE_OF_CONDUCT.md). Security vulnerabilities do **not** go in public issues — use the private channel described in [`SECURITY.md`](SECURITY.md).

## License

Guará's core — everything documented in this repository — is licensed under **[Apache-2.0](LICENSE)**: free use in commercial and proprietary applications, no obligation to open your own code, and an explicit patent grant. That includes **Native AOT and single-file** publishing, where the library is statically linked into your binary.

A small set of advanced add-ons (such as batch orchestration) is planned as separately licensed commercial `Guara.Pro.*` packages that help fund development; the core will always remain free and open source. See [ADR-0011](docs/adr/0011-licenca-apache-e-assinatura-de-assembly.md).

All assemblies ship **strong-named** (binding identity — not a security mechanism).
