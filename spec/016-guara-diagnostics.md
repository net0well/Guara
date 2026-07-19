# Spec 016: `Guara.Diagnostics` — Logging, Metrics, Tracing, HealthChecks

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Diagnostics`
**Depende de:** [Spec 001](001-guara-abstractions.md), [Spec 002](002-guara-core.md)
**Docs de referência:** [execution-flows](../docs/execution-flows.md) · [ADR-0007](../docs/adr/0007-pipeline-de-middlewares.md)

## Problem

Operar um scheduler em produção exige **observabilidade**: saber quantos jobs rodam, quanto demoram, quantos falham, e se o servidor está saudável. Reaproveitando o BCL (decisão DD-3 da Spec 001), `Guara.Diagnostics` fornece os **middlewares** de Logging/Metrics/Tracing (slots do pipeline) e os **HealthChecks**, sem inventar abstrações próprias de log.

## Scope

### In

- **`LoggingMiddleware`** (slot Logging) usando `Microsoft.Extensions.Logging` com **logging estruturado**: propriedades nomeadas (`JobId`, `Queue`, `JobType`, `Attempt`, `DurationMs`, `State`) via message templates + `ILogger.BeginScope` — nunca interpolação de string.
- O framework **não força sink**: loga via `ILogger`; o host escolhe o destino. O `Guara.Host` de exemplo usa o **JSON console formatter nativo** (`Microsoft.Extensions.Logging.Console`) — logs estruturados no stdout, sem terceiros ([ADR-0009](../docs/adr/0009-politica-de-dependencias.md)). Sinks de terceiros (Serilog→Seq, OpenTelemetry Logs, etc.) são opções do usuário, fora do framework.
- **`MetricsMiddleware`** (slot Metrics) usando `System.Diagnostics.Metrics` (contadores/histogramas).
- **Tracing** via `System.Diagnostics.ActivitySource` (spans por job).
- **HealthChecks** (`Microsoft.Extensions.Diagnostics.HealthChecks`): storage acessível, servidor vivo, filas dentro de limites.
- Convenções de nomes de métricas/atividades (namespace `guara.*`).

### Out

- Exporters concretos (OTLP/Prometheus) → `Guara.OpenTelemetry` (Spec 017).
- Endpoint HTTP `/health` (é wiring do host/ASP.NET, orientado pela skill `dotnet-claude-kit:logging`).

## Domain Model

- Middlewares nos slots canônicos do pipeline (Core, Spec 002).
- **Métricas**: `guara.jobs.processed` (counter), `guara.jobs.failed` (counter), `guara.job.duration` (histogram), `guara.queue.length` (observable gauge).
- **Atividades**: `ActivitySource("Guara")`, span por execução de job com tags (`job.id`, `job.queue`, `job.state`, `job.attempt`).

## API Contract

```csharp
namespace Guara.Diagnostics;

public sealed class LoggingMiddleware(ILogger<LoggingMiddleware> logger) : IJobMiddleware { /* ... */ }
public sealed class MetricsMiddleware(IMeterFactory meters) : IJobMiddleware { /* ... */ }

public static class GuaraDiagnostics
{
    public const string ActivitySourceName = "Guara";
    public const string MeterName = "Guara";
}
```

`UseGuaraDiagnostics()` (extensão única) registra os middlewares e os health checks.

## Authorization

N/A. Logs **não** devem vazar payloads sensíveis (regra: logar `JobId`/tipo, não argumentos por padrão).

## Edge Cases & Failure Modes

- **Log de dados sensíveis** → argumentos não são logados por padrão; opt-in explícito.
- **Métrica de alta cardinalidade** → tags controladas (sem `job.id` em métricas agregadas, só em traces).
- **HealthCheck de storage lento** → timeout curto; degradado em vez de travar.
- **Middleware de diagnostics nunca quebra o job** → falha de logging/métrica é engolida (best-effort).

## Non-Functional Requirements

- Overhead mínimo no hot path; sem alocação por job além do necessário.
- Reutiliza BCL (M.E.Logging, System.Diagnostics) — AOT-safe.
- **Logging estruturado** com propriedades e escopos (sink-agnóstico); templates sem interpolação para preservar as propriedades em qualquer sink (JSON console, Seq, OTel, ELK...).
- Thread-safe; instrumentos de métrica são singletons.

## Integrations

Integra-se a qualquer backend via os padrões do .NET; `Guara.OpenTelemetry` (Spec 017) exporta. HealthChecks integram ao `/health` do host.

## Acceptance Criteria

- **AC-1 — Métricas.** *Dado* N jobs processados, *então* `guara.jobs.processed` incrementa N e `guara.job.duration` registra durações.
- **AC-2 — Falhas.** *Dado* um job que falha, *então* `guara.jobs.failed` incrementa e um log de erro estruturado é emitido.
- **AC-3 — Trace.** *Dado* a execução de um job, *então* existe um span em `ActivitySource("Guara")` com tags de contexto.
- **AC-4 — HealthCheck.** *Dado* storage indisponível, *então* o health check reporta unhealthy/degraded (não trava).
- **AC-5 — Sem vazar payload.** *Dado* logging default, *então* argumentos do job não aparecem nos logs.
- **AC-6 — Não quebra job.** *Dado* falha ao emitir métrica/log, *então* o job continua normalmente.
- **AC-7 — Baixa cardinalidade.** *Dado* as métricas, *então* nenhuma usa `job.id` como tag.
- **AC-8 — Logs estruturados.** *Dado* um job processado, *então* os logs contêm `JobId`/`Queue`/`JobType`/`Attempt`/`State` como **propriedades estruturadas** (não texto interpolado), consumíveis por qualquer sink (JSON console nativo, Seq, OTel...).

## Deferred Decisions

- **DD-1 — Conjunto exato de métricas.** *Fallback:* processed/failed/duration/queue.length no MVP; expandir conforme necessidade. *Revisão:* pós-MVP.
- **DD-2 — Log de argumentos.** *Fallback:* off por padrão; opt-in com redaction. *Revisão:* Spec 020/021 (segurança).

> **Implementação (2026-07-19) — middlewares:** `UseGuaraDiagnostics()` registra três middlewares no pipeline (ordem: **Tracing → Logging → Metrics** — logs saem correlacionados ao span): `TracingMiddleware` (span `guara.job` na fonte `Guara`, tags `job.id/queue/type/attempt`, status Error com a mensagem na falha; custo ~zero sem listener — AC-3), `LoggingMiddleware` (escopo estruturado `JobId/Queue/JobType/Attempt` + desfecho com duração; argumentos nunca logados — AC-5/AC-8; funciona sem `AddLogging` via NullLogger) e `MetricsMiddleware` (`guara.jobs.processed` contador com `queue`+`outcome` success/failure/canceled e `guara.job.duration` histograma por fila — AC-1/AC-7; falha definitiva vs retentativa é distinguível pelos eventos `JobRetryScheduled`/`JobFailed`, então `guara.jobs.failed` separado foi absorvido pela tag `outcome`). Exceções são relançadas — o executor decide estado (o middleware não engole falha de job; AC-6 vale para falha DO instrumento, e instrumentos BCL não lançam no caminho de emissão). Os middlewares entram no slot `Custom` do pipeline (os slots dedicados Logging/Metrics do Core passam a ser usados quando a composição por slot chegar à DI). **Pendentes para o incremento do host HTTP/dashboard:** HealthChecks (AC-4) e o gauge `guara.queue.length` (callback de gauge é síncrono; a leitura de fila é async — sai como polling leve junto do dashboard/API). Sample com JSON console nativo comprovando logs estruturados no stdout.

## Open Questions

_(vazio)_
