# Referência competitiva — Hangfire × Quartz.NET × Guará

Documentos de referência de implementação, extraídos da análise dos repositórios originais (**Hangfire** — LGPL; **Quartz.NET** — Apache-2.0). Servem de **guia de comportamento** para implementar o Guará com paridade (e superar onde possível), preservando nossas invariantes (`../ARCHITECTURE.md`, `../semantics.md`, ADR-0009): zero terceiros no núcleo, zero reflection/AOT-safe, API do usuário em português.

> **Como este material foi produzido.** Uma análise multi-agente (ultracode) leu os dois repositórios a fundo. A primeira execução esgotou o limite de sessão após produzir parte dos documentos; os demais serão completados numa nova execução (ver **Status** abaixo). Os docs concluídos são densos e citam arquivos reais.

## Status dos documentos

| Área | Arquivo | Status |
|---|---|---|
| Calendários | [calendarios.md](calendarios.md) | ✅ Completo |
| DI, Hosting e Configuração | [di-hosting-config.md](di-hosting-config.md) | ✅ Completo |
| Modelo de Plugins (Quartz) | [plugins.md](plugins.md) | ✅ Completo |
| Benchmarks (Quartz.Benchmark) | [benchmarks.md](benchmarks.md) | ✅ Completo |
| Storage e Fila (dequeue atômico, lease) | `storage-e-fila.md` | ⏳ Pendente (re-rodar) |
| Agendamento e Triggers | `agendamento-e-triggers.md` | ⏳ Pendente |
| Retry e Misfire | `retry-e-misfire.md` | ⏳ Pendente |
| Continuations | `continuations.md` | ⏳ Pendente |
| Concorrência, filtros e listeners | `concorrencia-e-filtros.md` | ⏳ Pendente |
| Cluster, locks, heartbeat/failover | `cluster-e-locks.md` | ⏳ Pendente |
| Servidor, loop, thread pool, shutdown | `servidor-e-processamento.md` | ⏳ Pendente |
| Dashboard e autenticação | `dashboard.md` | ⏳ Pendente |
| Observabilidade (OTel/logging) | `observabilidade.md` | ⏳ Pendente |
| Saúde e estrutura dos repos | `saude-e-estrutura.md` | ⏳ Pendente |

Para completar: re-executar o workflow de análise (após o reset do limite de sessão) apenas com as áreas pendentes.

---

## Matriz de paridade (visão geral)

Legenda: ✅ tem · ⚠️ parcial/planejado · ❌ não tem · 🟦 diferencial do Guará

| Funcionalidade | Hangfire | Quartz.NET | Guará (estado / plano) |
|---|---|---|---|
| Fire-and-forget | ✅ `Enqueue` | ⚠️ via trigger imediato | ✅ `EnfileirarAsync` (implementado) |
| Delayed (uma vez) | ✅ `Schedule` | ✅ `SimpleTrigger` | ✅ `AgendarAsync` (implementado) |
| Recorrente por cron | ✅ (Cronos, 5-6 campos) | ✅ (cron próprio, 6-7 campos, L/W/#) | ✅ cron **próprio** 5 campos (implementado); builder fluente (spec 038) |
| Recorrente por intervalo | ❌ | ✅ `Calendar/DailyTimeInterval` | ⚠️ `ACada`/`EntreHorarios` (spec 038) |
| Calendários (feriados/exclusões) | ❌ | ✅ 7 tipos de `ICalendar` | ⚠️ spec 038 + gestão no dashboard (spec 032) |
| Continuations / encadeamento | ✅ `ContinueJobWith` | ⚠️ só `JobChainingJobListener` | ⚠️ `ContinuarComAsync` (spec 030) |
| Batches (grupo + callback) | 💰 Pro | ❌ | 💰 spec 031 (Pro) |
| Retry automático | ✅ `AutomaticRetry` (persistente, máquina de estados) | ⚠️ misfire + `RefireImmediately` | ⚠️ in-process; **alvo persistente** (spec 008/semantics) |
| DisableConcurrentExecution | ✅ (lock distribuído) | ✅ `[DisallowConcurrentExecution]` | ⚠️ `[GuaraDesabilitarConcorrencia]` via `ILockProvider` (spec 036) |
| Tempo limite de job | ⚠️ `LatencyTimeout` | ✅ plugin auto-interrupt | ⚠️ `[GuaraTempoLimite]` cooperativo (spec 036) |
| Filas com prioridade | ✅ | ✅ (priority de trigger) | ✅ prioridade estrita (dispatcher, implementado) |
| Storage é a fila (sem broker) | ✅ | ⚠️ JobStore com estados de trigger | ✅ lease/visibility (implementado; Memory) 🟦 conformance kit |
| Providers de storage | SQL/Redis/... (pacotes) | RAM/ADO/Redis | Memory ✅; PG/SQL/Redis/Mongo (spec 011-015) 🟦 kit único |
| Cluster / failover | ✅ (sem líder, DB coordena, watchdog) | ✅ (checkin/recover, LOCKS table) | ⚠️ eleição de líder sobre `ILockProvider` (spec 025) |
| Dashboard | ✅ (Razor, tempo real por polling) | ⚠️ `Quartz.Dashboard` (comunidade) | ⚠️ Angular SPA, **tempo real por SSE** (spec 022-024) 🟦 |
| Autenticação do dashboard | ✅ `IDashboardAuthorizationFilter` | — | ⚠️ regras fluentes + `IDashboardAccessRule` + login próprio (spec 037) 🟦 |
| Modelo de plugins | ❌ (filtros/`IBackgroundProcess`) | ✅ `ISchedulerPlugin` + listeners | ⚠️ event bus + middlewares + hosted services; `IGuaraPlugin` proposto (spec 039 futura) |
| Serialização | System.Text.Json / Newtonsoft, plugável | System.Text.Json / Newtonsoft, plugável via `IObjectSerializer` | ✅ **não é ponto de extensão**: o generator emite leitor e escritor tipados por job, e cada provider serializa o descritor com o próprio contexto STJ source-gen ([ADR-0019](../adr/0019-guara-serialization-sai-do-catalogo.md)) 🟦 AOT |
| Zero reflection / AOT | ❌ (reflection) | ⚠️ | ✅ source generators (spec 029) 🟦 |
| OpenTelemetry | ⚠️ (métricas no dashboard) | ✅ `Quartz.OpenTelemetry.Instrumentation` | ⚠️ `Guara.OpenTelemetry` (spec 017) |
| CLI | ❌ | ⚠️ `Quartz.Server` | ⚠️ `guara` (spec 027) |
| Benchmarks formais | ❌ | ✅ `Quartz.Benchmark` (BDN) | ⚠️ `guara/benchmarks` (spec 033/034) |
| Analyzers de arquitetura | ❌ | ⚠️ FxCop | ⚠️ `Guara.Analyzers` GUARA* (spec 028) 🟦 |
| API do usuário em português | ❌ | ❌ | 🟦 ADR-0010 |

**Leitura estratégica.** Onde já batemos de frente hoje: cron próprio, storage-é-a-fila com conformance kit, serialização AOT/segura, dashboard em tempo real (SSE vs polling). Onde precisamos alcançar: retry **persistente** (Hangfire é a referência — máquina de estados), calendários + intervalo (Quartz é a referência), cluster com failover, e o dashboard completo. Diferenciais que nenhum dos dois tem: AOT-first, zero-reflection, conformance kit de provider, autenticação de dashboard fluente, API em português.
