<p align="center">
  <img src="assets/logo-escrita.png" alt="Guará — Job Scheduler" width="440">
</p>

<p align="center">
  <strong>Background jobs and scheduling for modern .NET — component-based, storage-agnostic, AOT-ready.</strong>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache--2.0-blue" alt="License: Apache-2.0"></a>
  <img src="https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4" alt=".NET 8.0 | 10.0">
  <img src="https://img.shields.io/badge/status-under%20active%20development-orange" alt="Status: under active development">
</p>

<p align="center">
  <a href="README.md">Português (Brasil)</a> | English
</p>

---

> **Project status.** Guará is under active development and **has not been published to NuGet yet**. The runtime, the PostgreSQL storage and the full dashboard are implemented and covered by tests; anything not there yet is marked _planned_ throughout this document and in the [roadmap](#roadmap). Star and watch the repository to follow the progress.

## Packages

Install only what you use — the core runs on its own and everything else is optional. **Nothing is published yet**: the state column says what already exists in the repository and will ship in the first release.

| Package | Purpose | State |
|---|---|---|
| `Guara.Hosting` | Entry point: `AddGuara()` and the fluent builder | ✅ implemented |
| `Guara.Server` | Lifecycle: workers, scheduler, heartbeat, maintenance | ✅ implemented |
| `Guara.Scheduler` | Own cron, recurring jobs, calendars, `IGuaraClient` | ✅ implemented |
| `Guara.Storage.PostgreSql` | PostgreSQL storage — recommended for production | ✅ implemented |
| `Guara.Storage.Memory` | In-memory storage — dev, tests and demos | ✅ implemented |
| `Guara.Dashboard` | Web dashboard (API + embedded Angular SPA, real-time) | ✅ implemented |
| `Guara.Authorization` | Per-action dashboard permissions | ✅ implemented |
| `Guara.Diagnostics` | Structured logs, metrics and traces | ✅ implemented |
| `Guara.SourceGenerators` | Reflection-free job registration and invocation | ✅ implemented |
| `Guara.Abstractions` / `Guara.Storage` | Contracts — for provider and extension authors | ✅ implemented |
| `Guara.Storage.SqlServer` | SQL Server storage | 🕓 planned |
| `Guara.Storage.MySql` | MySQL 8+ storage | 🕓 planned |
| `Guara.Storage.Mongo` | MongoDB storage | 🕓 planned |
| `Guara.Storage.Redis` | Redis storage | 🕓 planned |
| `Guara.Authentication` | Authentication schemes (JWT, OIDC, cookie) | 🕓 planned |
| `Guara.Cluster` / `Guara.Distributed` | Leader election, failover, distributed coordination | 🕓 planned |
| `Guara.OpenTelemetry` | OpenTelemetry exporters | 🕓 planned |
| `Guara.Cli` | Command-line tool (`dotnet tool`) | 🕓 planned |
| `Guara.Analyzers` | Roslyn analyzers enforcing the dependency rules | 🕓 planned |
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
| Pluggable storage | Shared contracts with a conformance kit every provider inherits. Today: PostgreSQL and In-Memory — switch with one line |
| Observability | Structured logs, metrics (`System.Diagnostics.Metrics`), traces (`ActivitySource`) |
| Secure by default | Dashboard denies anonymous access unless explicitly configured otherwise; whatever was not granted is denied |
| _Planned_ | Distributed processing (leader election, failover), remaining storage providers, OpenTelemetry exporters, CLI and Roslyn analyzers |

## Quick start

With the packages installed (see [Packages](#packages)), configure everything with a fluent, ASP.NET Core-style API:

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

### Recurring jobs (fluent builder, Quartz-style)

Recurring jobs are configured through a **fluent builder** ([spec 038](spec/038-agendamento-fluente.md)) — identity, schedule, validity window, description, and calendar in one place (`ComId` = with id, `IniciaEm` = start at, `ACada` = every, `ComCalendario` = with calendar):

```csharp
await jobs.AdicionarOuAtualizarRecorrenteAsync(job => job
    .ComId("nightly-cleanup")
    .Executa(() => CleanupExpiredRecordsAsync())
    .ComCron("0 3 * * *")                                          // every day at 03:00
    .NoFusoHorario("America/Sao_Paulo")                            // accepts IANA or Windows ids
    .IniciaEm(GuaraDatas.SegundoExato(DateTimeOffset.UtcNow.AddSeconds(7)))
    .ComDescricao("Removes expired records")
    .ComCalendario("holidays"),                                    // skips excluded dates
    ct);

// Interval-based schedule (no cron), with a daily window
await jobs.AdicionarOuAtualizarRecorrenteAsync(job => job
    .ComId("price-sync")
    .Executa(() => SyncPricesAsync())
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

| Provider | Atomic dequeue | Distributed lock | State |
|---|---|---|---|
| PostgreSQL | `FOR UPDATE SKIP LOCKED` | Advisory locks | ✅ implemented, conformance green |
| In-Memory | Mutual exclusion over the dictionary | Process-local | ✅ implemented, conformance green |
| SQL Server | `READPAST + UPDLOCK` | `sp_getapplock` | 🕓 planned |
| MySQL 8+ | `FOR UPDATE SKIP LOCKED` | `GET_LOCK` | 🕓 planned |
| MongoDB | `findAndModify` | TTL collection | 🕓 planned |
| Redis | Lua scripts | `SET NX PX` + TTL | 🕓 planned (scope under review) |

Switching provider is a one-line change:

```csharp
builder.Services.AddGuara().UseMemoryStorage();                    // dev/tests
builder.Services.AddGuara().UsePostgreSqlStorage(connectionString); // production
```

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
- **Time series**: the dashboard API aggregates throughput, success/failure and p50/p95 latency per bucket, over 1h, 24h and 7d windows.
- **OpenTelemetry** _(planned)_: an optional package will register Guará's sources into your existing OTel pipeline. The core never forces an exporter on you — the sources already exist today and can be consumed directly.

## Roadmap

| Milestone | Status |
|---|---|
| Architecture documentation and ADRs | ✅ Done |
| Full specification (40 specs, one per component/feature) | ✅ Done |
| Foundation: `Guara.Abstractions` + `Guara.Core` (pipeline, state machine, events) | ✅ Done |
| Serialization (`Guara.Serialization` — source-gen, allowlist) | ✅ Done |
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
| Public API freeze (`PublicApiAnalyzers`) | 🔨 In progress |
| CI/CD: multi-TFM build, containerised conformance, publish on tag | 🔨 In progress |
| **First NuGet release (`0.1.0-preview`)** | 🕓 Next |
| Remaining providers: SQL Server → MySQL → MongoDB → Redis | 🕓 Planned |
| `Guara.Analyzers`, `Guara.Extensions`, `Guara.Authentication` | 🕓 Planned |
| Cluster and distributed coordination, OpenTelemetry, CLI, benchmarks | 🕓 Planned |
| User documentation and Hangfire migration guide | 🕓 Planned |
| **1.0** | 🕓 Planned |

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
