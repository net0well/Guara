# CLAUDE.md — Guará

Guia para agentes de IA (Claude Code) trabalhando neste repositório. **Leia antes de qualquer alteração.**

## O que é o Guará

Framework de agendamento e execução de tarefas (job scheduler, tipo Hangfire) **orientado a componentes**, inspirado em ASP.NET Core, EF Core, Hangfire e MediatR. Backend em **.NET 10**; dashboard SPA em **Angular**.

A arquitetura é **lei** e está documentada em [`docs/`](docs/ARCHITECTURE.md). Antes de escrever código, o agente **deve** conhecer:

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — hub: filosofia, dependências, estrutura, nomenclatura, fluxos, performance, checklist.
- [docs/components.md](docs/components.md) · [docs/dependency-rules.md](docs/dependency-rules.md) · [docs/naming-conventions.md](docs/naming-conventions.md)
- [docs/execution-flows.md](docs/execution-flows.md) · [docs/patterns.md](docs/patterns.md) · [docs/anti-patterns.md](docs/anti-patterns.md)
- [docs/performance.md](docs/performance.md) · [docs/checklist.md](docs/checklist.md) · [docs/adr/](docs/adr/README.md)

Idioma da documentação e das mensagens ao usuário: **português (pt-BR)**.

---

## ⚠️ Regra inegociável: uso obrigatório de Skills

**Nenhum trabalho técnico começa sem antes carregar a(s) skill(s) correspondente(s) e declarar explicitamente qual está sendo usada.** Não escreva código de memória.

Formato da declaração (sempre antes de agir):

> 🔧 **Skill em uso:** `dotnet-claude-kit:minimal-api` — motivo: criar endpoints do Dashboard.Api.

### Backend (.NET 10) → **dotnet-claude-kit** (obrigatório)

As skills do `dotnet-claude-kit` são plugins instalados e se invocam pela ferramenta **Skill** (ou `/dotnet-claude-kit:<nome>`). **Sempre** carregue a skill pertinente antes de tocar em código .NET.

| Tarefa no Guará | Skill obrigatória |
|---|---|
| Escolher/validar arquitetura de um projeto ou fronteira de componente | `dotnet-claude-kit:architecture-advisor` |
| Criar/iniciar projeto ou solution (ver seção abaixo) | `dotnet-claude-kit:project-setup`, `dotnet-claude-kit:dotnet-init`, `dotnet-claude-kit:project-structure`, `dotnet-claude-kit:scaffold` |
| C# moderno (records, pattern matching, `Span`, `ValueTask`) | `dotnet-claude-kit:modern-csharp` |
| Endpoints HTTP (Dashboard.Api) | `dotnet-claude-kit:minimal-api` |
| OpenAPI / documentação de API | `dotnet-claude-kit:openapi`, `dotnet-claude-kit:scalar` |
| Versionamento de API | `dotnet-claude-kit:api-versioning` |
| Injeção de dependência / extensões `AddGuara...()` | `dotnet-claude-kit:dependency-injection` |
| Configuração / Options | `dotnet-claude-kit:configuration` |
| Tratamento de erros / Result / ProblemDetails | `dotnet-claude-kit:error-handling` |
| Persistência dos providers `Guara.Storage.*` (EF Core) | `dotnet-claude-kit:ef-core` |
| Migrations / upgrades / dependências | `dotnet-claude-kit:migrate` |
| Cache (dashboard, leitura) | `dotnet-claude-kit:caching` |
| Mensageria / eventos entre componentes | `dotnet-claude-kit:messaging` |
| Resiliência / retry / circuit breaker | `dotnet-claude-kit:resilience` |
| HttpClient tipado (integrações) | `dotnet-claude-kit:httpclient-factory` |
| Autenticação / Autorização (`Guara.Authentication/.Authorization`) | `dotnet-claude-kit:authentication` |
| Logging / observabilidade (`Guara.Diagnostics`) | `dotnet-claude-kit:logging`, `dotnet-claude-kit:opentelemetry` — logging estruturado **nativo** (`ILogger` + JSON console); **sem Serilog/sinks de terceiros** no framework (ADR-0009) |
| Orquestração local / dev (`Guara.OpenTelemetry`, samples) | `dotnet-claude-kit:aspire` |
| Docker / publicação em container | `dotnet-claude-kit:docker`, `dotnet-claude-kit:container-publish` |
| CI/CD | `dotnet-claude-kit:ci-cd` |
| Testes (unit/integração/TDD) | `dotnet-claude-kit:testing`, `dotnet-claude-kit:tdd` |
| Revisão de código | `dotnet-claude-kit:code-review` |
| Verificar/validar mudança end-to-end | `dotnet-claude-kit:verify` |
| Consertar build/testes quebrados | `dotnet-claude-kit:build-fix` |
| Limpeza / dead code / warnings | `dotnet-claude-kit:de-sloppify` |
| Segurança / auditoria | `dotnet-claude-kit:security-scan` |
| Saúde do projeto | `dotnet-claude-kit:health-check` |
| Aprender/enforçar convenções do repo | `dotnet-claude-kit:convention-learner` |
| Escrever spec / planejar | `dotnet-claude-kit:spec`, `dotnet-claude-kit:plan` |

Agentes especialistas do kit (via ferramenta Agent) para trabalho profundo: `dotnet-claude-kit:dotnet-architect`, `:api-designer`, `:ef-core-specialist`, `:performance-analyst`, `:security-auditor`, `:test-engineer`, `:code-reviewer`, `:build-error-resolver`, `:refactor-cleaner`, `:devops-engineer`.

Análise de código com Roslyn: use as ferramentas MCP `cwm-roslyn-navigator` (`find_symbol`, `find_references`, `detect_antipatterns`, `detect_circular_dependencies`, `get_dependency_graph`, etc.) para **verificar as regras de dependência** de [docs/dependency-rules.md](docs/dependency-rules.md).

### Frontend (Angular — `Guara.Dashboard.Angular`) → **skills Angular** (obrigatório)

As skills oficiais do Angular Team estão **instaladas** em `~/.claude/skills/` (via `npx skills add angular/skills`) e se invocam pela **ferramenta Skill**. **Sempre** carregue a skill pertinente antes de tocar em código Angular e declare o uso.

| Tarefa no Dashboard Angular | Skill obrigatória |
|---|---|
| Criar o app Angular do zero (Angular CLI, estrutura moderna) | `angular-new-app` |
| Componentes, services, reatividade (signals, linkedSignal, resource), forms, DI, routing, a11y (ARIA), animações, styling, testes, CLI | `angular-developer` |
| Gráficos/visualização de dados no dashboard | `dataviz` (paleta/acessibilidade) + `angular-developer` |

Diretrizes fixas do dashboard (spec 024): standalone components, **signals** como modelo reativo, `OnPush`/zoneless, lazy loading por rota, SSE via `EventSource`, WCAG/ARIA, i18n pt-BR/en, build por Angular CLI gerando assets estáticos embutidos.

---

## Criação de projeto / solution — qual skill usar

Ao criar o projeto ou qualquer novo pacote, **verifique e use a skill certa do dotnet-claude-kit** (nunca `dotnet new` "no braço" sem seguir as convenções):

1. **`dotnet-claude-kit:project-setup`** (ou `dotnet-claude-kit:dotnet-init`) — inicialização guiada do projeto e geração de estrutura/CLAUDE.md.
2. **`dotnet-claude-kit:project-structure`** — estrutura moderna: `.slnx`, `Directory.Build.props`, Central Package Management (CPM), `global.json`, SourceLink, versionamento.
3. **`dotnet-claude-kit:architecture-advisor`** — carregue **antes** de decisões de arquitetura. Observação: a arquitetura do Guará **já está definida** como **Component-Based** em [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) (não é VSA/Clean/DDD clássico). Use o advisor para validar fronteiras, mas **respeite o ARCHITECTURE.md** — ele prevalece.
4. **`dotnet-claude-kit:scaffold`** — gerar novos componentes/pacotes seguindo boas convenções e design patterns.
5. Para decisões estruturais mais profundas, acione o agente **`dotnet-claude-kit:dotnet-architect`**.

Todo novo pacote `Guara.*` deve nascer aprovado pelo [checklist](docs/checklist.md).

---

## Invariantes que o código deve respeitar (resumo)

Detalhe em [docs/](docs/ARCHITECTURE.md). Violações são anti-padrões ([docs/anti-patterns.md](docs/anti-patterns.md)) e várias quebram a build via `Guara.Analyzers`.

- **Um projeto = uma responsabilidade.** Nunca misturar (`Storage + Scheduler`, etc.).
- **Dependências unidirecionais:** `Dashboard → Api → Core → Abstractions`. `Abstractions` não depende de nada.
- **Só contratos:** componentes conhecem `IStorage`, `IScheduler`, etc. — nunca a classe concreta de outro componente.
- **Comunicação por evento/contrato** — nunca chamada direta entre componentes.
- **Um `AddGuara...()`/`Use...()` por pacote**, no namespace `Microsoft.Extensions.DependencyInjection`.
- **API do usuário em português** (ADR-0010): métodos do `IGuaraClient`/`IBatchClient` — `EnfileirarAsync`, `AgendarAsync`, `AdicionarOuAtualizarRecorrenteAsync`, `ExcluirAsync`, `ContinuarComAsync`. Tipos, DI, options, rotas, CLI e contratos internos permanecem em inglês.
- **Zero reflection em runtime** (Source Generators); `ValueTask` no hot path; `CancellationToken` propagado; **AOT/Trimming-safe**.
- Sem factory global estática, sem singleton estático, sem `.Result`/`.Wait()`/`Thread.Sleep`.

---

## Ordem de trabalho recomendada

1. Ler o(s) doc(s) relevante(s) em `docs/`.
2. **Declarar e carregar a skill** (dotnet-claude-kit para backend, skills Angular para o front).
3. Implementar seguindo os padrões ([docs/patterns.md](docs/patterns.md)).
4. Rodar o [checklist](docs/checklist.md) + `dotnet-claude-kit:verify`.
5. Revisar com `dotnet-claude-kit:code-review` antes do commit.

> As **specs** de features serão criadas depois desta documentação, usando `dotnet-claude-kit:spec`.
