# Specs do Guará

Especificações por componente, no formato do workflow `/spec` (dotnet-claude-kit). **Uma spec por pacote `Guara.*`**, alinhada a [../docs/components.md](../docs/components.md).

## Regras

- Ciclo de status: **Draft → In Review → Approved**. Implementação nunca parte de Draft.
- Uma spec por vez, com rounds de perguntas até as 9 dimensões estarem resolvidas. `Open Questions` deve estar **vazio** antes da aprovação.
- Toda spec referencia os docs de arquitetura; a arquitetura ([../docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md)) prevalece.

## Decisões de Produto (2026-07-16)

Alvo é o **produto completo 1.0** (não um MVP) — construído passo a passo. Onde as specs dizem "MVP", leia-se **"escopo 1.0"**.

| Decisão | Valor |
|---|---|
| **Target Frameworks** | Multi-target `net8.0` (LTS) + `net10.0`. AOT/trimming plenos no net10; funcional no net8. |
| **Licença** | **LGPL-3.0 (core aberto) + comercial ("Pro")** — modelo Hangfire. |
| **Recursos 1.0** | Continuations (OSS) · Batches (**Pro/comercial**) · Dashboard avançado (OSS). Multi-tenancy **fora do 1.0**. |
| **Split OSS × Pro** | OSS: todo o runtime, providers, dashboard (tempo real + avançado), continuations, cluster, CLI, analyzers, source gen. **Pro (comercial):** `Guara.Pro.Batches` (e futuros extras). *Proposta — me avise se quiser mover a fronteira.* |
| **Logs** | Estruturados via `ILogger` (sink-agnóstico, ADR-0009); `Guara.Host` usa o JSON console formatter **nativo** do .NET (sem Serilog); sinks/painéis de terceiros são opção do usuário. |
| **Semântica** | Garantias canônicas em [docs/semantics.md](../docs/semantics.md) (2026-07-17): entrega **at-least-once**; tempo limite cooperativo (job que ignora o token e completa = Succeeded+aviso); recorrentes **sobrepõem por padrão**; misfire = **uma** compensação; filas com **prioridade estrita** (starvation documentada); retentativa **persistente** como alvo. |

## Roadmap (ordem de dependência)

Trabalhamos de baixo para cima na pirâmide de dependências: contratos primeiro, depois motores, hosting, providers e superfícies externas.

| # | Spec | Componente | Status |
|---|---|---|---|
| 001 | Contratos, eventos e tipos-base | `Guara.Abstractions` | ✅ Approved (2026-07-16) |
| 002 | Modelos internos, estados e pipeline | `Guara.Core` | ✅ Approved (2026-07-16) |
| 003 | Serialização | `Guara.Serialization` | ✅ Approved (2026-07-16) |
| 004 | Contratos de storage | `Guara.Storage` | ✅ Approved (2026-07-16) |
| 005 | Cálculo de agendamento (cron/delay/recurring) | `Guara.Scheduler` | ✅ Approved (2026-07-16) |
| 006 | Busca de jobs | `Guara.Dispatcher` | ✅ Approved (2026-07-16) |
| 007 | Execução de jobs (capacidade) | `Guara.Worker` | ✅ Approved (2026-07-16) |
| 008 | Execução do job pronto | `Guara.Executor` | ✅ Approved (2026-07-16) |
| 009 | Hosting / DI / bootstrap | `Guara.Hosting` | ✅ Approved (2026-07-16) |
| 010 | Lifecycle do servidor / heartbeat | `Guara.Server` | ✅ Approved (2026-07-16) |
| 011 | Storage em memória | `Guara.Storage.Memory` | ✅ Approved (2026-07-16) |
| 012 | Storage SQL Server | `Guara.Storage.SqlServer` | ✅ Approved (2026-07-16) |
| 013 | Storage PostgreSQL | `Guara.Storage.PostgreSql` | ✅ Approved (2026-07-16) |
| 014 | Storage Redis | `Guara.Storage.Redis` | ✅ Approved (2026-07-16) |
| 015 | Storage MongoDB | `Guara.Storage.Mongo` | ✅ Approved (2026-07-16) |
| 016 | Diagnostics (log/metrics/tracing/health) | `Guara.Diagnostics` | ✅ Approved (2026-07-16) |
| 017 | Exporters OpenTelemetry | `Guara.OpenTelemetry` | ✅ Approved (2026-07-16) |
| 018 | Configuração / Options | `Guara.Configuration` | ✅ Approved (2026-07-16) |
| 019 | Extensões utilitárias | `Guara.Extensions` | ✅ Approved (2026-07-16) |
| 020 | Autenticação | `Guara.Authentication` | ✅ Approved (2026-07-16) |
| 021 | Autorização | `Guara.Authorization` | ✅ Approved (2026-07-16) |
| 022 | APIs do dashboard | `Guara.Dashboard.Api` | ✅ Approved (2026-07-16) |
| 023 | Composição do dashboard | `Guara.Dashboard` | ✅ Approved (2026-07-16) |
| 024 | SPA do dashboard | `Guara.Dashboard.Angular` | ✅ Approved (2026-07-16, revisada: React → Angular) |
| 025 | Cluster (leader election/failover) | `Guara.Cluster` | ✅ Approved (2026-07-16) |
| 026 | Coordenação distribuída | `Guara.Distributed` | ✅ Approved (2026-07-16) |
| 027 | CLI | `Guara.Cli` | ✅ Approved (2026-07-16) |
| 028 | Analisadores Roslyn | `Guara.Analyzers` | ✅ Approved (2026-07-16) |
| 029 | Source Generators | `Guara.SourceGenerators` | ✅ Approved (2026-07-16) |

### Recursos 1.0 e camada de publicação (adicionados 2026-07-16)

| # | Spec | Pacote / Escopo | Licença | Status |
|---|---|---|---|---|
| 030 | Continuations (encadeamento de jobs) | `Guara.Core`/`Guara.Scheduler` (feature) | OSS | In Review |
| 031 | Batches (grupos de jobs + callback) | `Guara.Pro.Batches` | **Comercial** | In Review |
| 032 | Dashboard avançado (busca/filtros/gráficos ao vivo/bulk) | `Guara.Dashboard.*` (feature) | OSS | In Review |
| 033 | Empacotamento, Build & Versionamento | solution-wide (CPM, SourceLink, símbolos, API-compat) | — | In Review |
| 034 | CI/CD & Release (NuGet publish) | pipeline | — | In Review |
| 035 | Governança, Licenciamento & Docs | repositório | — | In Review |
| 036 | Atributos de Job (`[GuaraFila]`, `[GuaraRetentativas]`, `[GuaraDesabilitarConcorrencia]`, `[GuaraTempoLimite]`, `[GuaraPularSeAnteriorEmExecucao]`) | `Guara.Abstractions` + pipeline/generator (feature) | OSS | ✅ Approved (2026-07-16) |
| 037 | Dashboard — autenticação: regras fluentes (`UseGuaraAuthentication`), `IDashboardAccessRule`/`DashboardContext`, login fixo, página de login com a logo | `Guara.Dashboard.*` (feature) | OSS | ✅ Approved (2026-07-16) |
| 038 | Agendamento fluente — builder estilo Quartz, `GuaraDatas`, calendários, fuso IANA/Windows nativo | `Guara.Scheduler`/`Guara.Extensions` (feature) | OSS | ✅ Approved (2026-07-16) |
| 039 | Modelo de plugins (`IGuaraPlugin`) — composição de handlers/middlewares/hosted services; opt-in, pós-1.0 | `Guara.Abstractions` + `Guara.Plugins.*` | OSS | ✅ Approved (2026-07-18) |
| 040 | Storage MySQL (8+, `FOR UPDATE SKIP LOCKED` — blueprint da spec 013) | `Guara.Storage.MySql` | OSS | Draft (2026-07-19) |

> **Benchmarks** não têm spec própria: a suíte `guara/benchmarks/` (BenchmarkDotNet, net8+net10) está no escopo das specs 033/034 e detalhada em [docs/reference/benchmarks.md](../docs/reference/benchmarks.md) (espelhando `Quartz.Benchmark`).

> A ordem pode ser ajustada; capacidades transversais da API pública (fire-and-forget, delayed, recurring, retry) são especificadas dentro das specs dos componentes que as realizam (principalmente 001, 005, 008, 009). As specs 030–035 cruzam vários pacotes por serem **features/infra transversais**.

### Sequência acordada das próximas fases (2026-07-19)

1. **Fase D** — source generators (029) + `Enfileirar` tipado (019) + atributos de job (036).
2. **Fase E** — dashboard (022 → 023 → 024 → 032 → 037) + push LISTEN/NOTIFY (006+013).
3. **`samples/Guara.Example`** (decisão do autor, 2026-07-19): app simples (MVC) exercitando **todas** as funcionalidades, inclusive o dashboard — bancada de teste real e exemplo público de uso. `IsPackable=false` (nunca vira pacote); vive em `samples/`, que nenhum projeto `src/` referencia — zero impacto em performance ou nos pacotes.
4. **Fase F — providers restantes**, nesta ordem: SQL Server (012) → **MySQL (040)** → MongoDB (015) → Redis (014, re-escopar: storage primário em Redis é frágil — decidir se vira acelerador/cache ou provider pleno). Cada um herda o conformance kit (`tests/Guara.Storage.Conformance`) e segue o blueprint do PostgreSQL.
