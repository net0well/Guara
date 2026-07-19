# Spec 024: `Guara.Dashboard.Angular` — SPA do Dashboard

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16 (revisada 2026-07-16: stack trocada de React para **Angular** — decisão do produto: robustez/confiabilidade)
**Componente:** `Guara.Dashboard.Angular`
**Depende de:** [Spec 022 (`Guara.Dashboard.Api`)](022-guara-dashboard-api.md) — **somente** via contrato HTTP
**Skills obrigatórias (front):** `angular-developer` · `angular-new-app` (instaladas em `~/.claude/skills/`, invocadas pela ferramenta Skill — declarar uso antes de codar)

> **Atualização 2026-07-16:** atualização em **tempo real** promovida de *Deferred* para **escopo do MVP** — o painel reflete mudanças ao vivo via SSE.

## Problem

Operadores precisam de uma interface para **observar e agir** sobre os jobs: ver filas, jobs por estado, detalhes, retentar/excluir, acompanhar servidores. A SPA Angular consome **apenas** a API (Spec 022), sem qualquer conhecimento interno do framework — trocar a API ou a UI não deve afetar o outro lado. Sendo open-source, a UI precisa ser acessível, performática e confiável em instalações variadas.

## Scope

### In

- SPA **Angular** (TypeScript, standalone components, **signals**) consumindo a API v1 (Spec 022).
- Telas: **Visão geral** (contadores/gráficos), **Jobs** (lista paginada + filtros por estado/fila), **Detalhe do job** (com payload sob permissão), **Recorrentes**, **Servidores/Nós**.
- Ações: retentar, disparar, excluir (conforme permissões retornadas pela API).
- **Atualização em tempo real** via SSE (`/api/v1/stream`): contadores, listas e detalhe refletem mudanças sem refresh (com fallback para polling).
- Tema claro/escuro, i18n (pt-BR/en), acessibilidade (WCAG/ARIA).
- Build (Angular CLI) que gera **assets estáticos** embutidos por `Guara.Dashboard` (Spec 023).
- Telas avançadas (busca/filtros, gráficos ao vivo, gestão de recorrentes, **calendários** — visão mensal leve para feriados/exclusões — e ações em massa) → [Spec 032](032-dashboard-avancado.md).

### Out

- Qualquer lógica de negócio/scheduler (fica no backend).
- Acesso a storage ou conhecimento de providers.
- Autenticação própria (usa a sessão/esquema exposto pela API — Spec 020).
- SSR/hydration — o dashboard é servido como assets estáticos embutidos (Spec 023); SSR não se aplica.

## Domain Model (client-side)

- Estado do servidor em **signals** (`signal`/`computed`/`resource`), separado do estado de UI; sem store global desnecessária.
- Camada de **data access** tipada sobre a API (services Angular + `HttpClient`/`fetch`), com invalidação após ações.
- Componentes **standalone** com change detection `OnPush`/zoneless; composição por `input()`/`output()` tipados — sem proliferação de flags booleanas.
- Eventos SSE integrados como signal (serviço `EventSource` → signals), alimentando contadores/listas reativamente.

## API Contract

Consome os endpoints da [Spec 022](022-guara-dashboard-api.md) (`/api/v1/*`). Contrato HTTP é a **única** dependência. Erros exibidos a partir de `ProblemDetails`.

## Authorization

A UI **reflete** as permissões vindas da API (esconde/desabilita ações sem permissão), mas a **decisão real é do backend** (Spec 021) — a UI nunca é a fronteira de segurança. Chamadas 401 → fluxo de login; 403 → feedback claro.

## Edge Cases & Failure Modes

- **API indisponível** → estados de erro/skeleton, retry com back-off; nunca tela branca.
- **Listas grandes** → paginação/virtualização (CDK Virtual Scroll); nunca carregar tudo.
- **Permissão ausente** → ação escondida/desabilitada com tooltip explicativo.
- **Payload sensível** → só exibido se a API o retornar (permissão `guara:view-payload`).
- **Provider com consulta limitada** (ex.: Redis) → UI adapta recursos conforme `stats`/`capabilities`.
- **Atualização ao vivo** → SSE via `EventSource` (reconexão automática); se o stream cair, fallback automático para polling — nunca fica desatualizado silenciosamente.

## Non-Functional Requirements

- **Performance** (skill `angular-developer`): lazy loading por rota, change detection `OnPush`/zoneless, `@defer` para blocos pesados, bundle enxuto (esbuild), trackBy em listas.
- **Reatividade** (skill `angular-developer`): signals/`computed`/`resource` como modelo padrão; sem subscriptions manuais vazando.
- **UX/Acessibilidade**: navegação por teclado, contraste, ARIA, foco visível, responsivo (diretrizes de a11y da skill `angular-developer`).
- **i18n** pt-BR/en; tema claro/escuro; SPA sem dependência de servidor de UI (arquivos estáticos).
- Criação/estrutura do app via skill `angular-new-app` (Angular CLI, projeto standalone moderno).

## Integrations

Somente a API HTTP (Spec 022). Empacotada e servida por `Guara.Dashboard` (Spec 023) como assets embutidos.

## Acceptance Criteria

- **AC-1 — Só API.** *Dado* o código da SPA, *então* toda comunicação com o backend passa pelo contrato HTTP `/api/v1` — sem acoplamento interno.
- **AC-2 — Lista paginada.** *Dado* muitos jobs, *então* a UI pagina/virtualiza; nunca carrega tudo de uma vez.
- **AC-3 — Ações refletem permissão.** *Dado* usuário sem `guara:delete`, *então* a ação Excluir não aparece/está desabilitada.
- **AC-4 — Erros claros.** *Dado* API 4xx/5xx, *então* a UI mostra mensagem a partir de `ProblemDetails`, sem quebrar.
- **AC-5 — Acessibilidade.** *Dado* uma auditoria WCAG básica, *então* navegação por teclado e contraste passam.
- **AC-6 — Performance.** *Dado* o bundle, *então* há lazy loading por rota e o first load fica dentro de um orçamento razoável.
- **AC-7 — Reatividade por signals.** *Dado* o código da SPA, *então* o estado reativo usa signals (sem stores/subscriptions manuais desnecessárias).
- **AC-8 — Build embutível.** *Dado* o build de produção (Angular CLI), *então* gera assets estáticos consumíveis pela Spec 023.
- **AC-9 — Tempo real.** *Dado* o painel aberto, *quando* um job muda de estado no servidor, *então* a UI atualiza em ~1s via SSE, sem refresh manual; se o SSE cair, o polling assume.

## Deferred Decisions

- **DD-1 — Stack de UI/build (resolvido 2026-07-16).** *Decisão:* **Angular** (versão estável mais recente) + TypeScript + Angular CLI; standalone components, signals, zoneless quando estável. Substitui a decisão anterior (React + Vite + TanStack) por diretriz do produto — Angular considerado mais robusto/confiável para este dashboard.
- **DD-2 — Tempo real (resolvido).** *Decisão:* **SSE no MVP** consumindo `/api/v1/stream` (Spec 022) via `EventSource`, com reconexão automática e fallback para polling. WebSocket/SignalR só se necessário.
- **DD-3 — Gráficos.** *Fallback:* biblioteca de charts acessível e leve (ou SVG próprio); seguir a skill `dataviz` para paleta/acessibilidade. *Revisão:* implementação.
- **DD-4 — Biblioteca de componentes.** *Fallback:* Angular Material (oficial, acessível) ou componentes próprios enxutos; decidir na implementação com a skill `angular-developer`. *Revisão:* implementação.

> **Implementação (2026-07-19):** SPA entregue em `src/guara-dashboard-angular` (Angular 19.2 standalone + **zoneless** `provideExperimentalZonelessChangeDetection` + **signals**, novo control-flow `@if`/`@for`, `input()` de rota, `inject()`; skill `angular-new-app` usada no scaffold). **DD-4 resolvida:** componentes **próprios enxutos** (sem Angular Material) e **DD-3:** donut de distribuição em **SVG inline** (sem lib de charts) — decisão do autor por bundle mínimo e identidade Guará (ADR-0009). Telas lazy por rota: Visão geral (contadores + donut SVG + filas), Jobs (paginado + filtros estado/fila, AC-2), Detalhe (ações retry/trigger/excluir refletindo o estado, AC-3), Recorrentes, Servidores. **Tempo real (AC-9):** `SseService` conecta `EventSource` em `api/v1/stream` → signals; entrega instantânea (`lastEvent` = indicador ao vivo) + `refresh` **coalescido** (máx. 1/s, não martela a API sob rajada) que os `resource()` observam para recarregar; queda do SSE → **poll de fallback** (5s). Erros via `ProblemDetails` (`describeError`, AC-4); estado reativo 100% em `resource()`/`computed`/`signal` (AC-7); tema claro/escuro (data-theme + prefers-color-scheme) e i18n pt-BR/en por signals (dicionário próprio — i18n de compilação do Angular geraria bundle por locale; escolha enxuta documentada); a11y básica (ARIA, foco visível, teclado, contraste — AC-5; auditoria Lighthouse formal fica no CI/spec 034). Bundle: **first load 252 kB raw / 70,6 kB transfer**, chunks lazy por rota (AC-6). **Base-path-agnóstica:** todas as chamadas (HttpClient + EventSource) são **relativas** ao `<base href>`, que o servidor reescreve para o BasePath real — o mesmo bundle serve em `/guara`, `/painel`, etc. (AC-1: só o contrato HTTP). **Empacotamento/embed (AC-8, spec 023):** alvo MSBuild em `Guara.Dashboard` (`BuildAngularDashboard` incremental via Inputs/Outputs + `EmbedAngularDashboard`) constrói a SPA quando há Node e embute o `dist` como recursos `Guara.Dashboard.Spa.*`; `DashboardSpa` serve o index (com `<base href>` reescrito), os assets com hash (cache imutável) e faz fallback de deep-link para o index — tudo sob o portão de acesso; **sem Node**, aviso no build e o dashboard serve a página placeholder (`SkipAngularBuild=true` pula). Verificação: 3 testes de integração .NET servindo a SPA real (index/asset/deep-link/rewrite de base) + build de produção Angular verde. **Fora deste incremento:** dashboard **avançado** (busca/filtros ricos, gráficos ao vivo, ações em massa, gestão de calendários) → [Spec 032](032-dashboard-avancado.md); virtualização (CDK) — a paginação atende o 1.0; permissões finas `guara:*` na UI quando a [Spec 021](021-guara-authorization.md) expor as flags; testes unitários Angular (karma) não gerados (`--skip-tests`) — a verificação é pelos testes de integração que servem a SPA. **Nota:** a pasta ficou `guara-dashboard-angular` (kebab-case, convenção do Angular; rename cosmético bloqueado por lock de arquivo no Windows) — o pacote .NET dono do embed é `Guara.Dashboard`.

## Open Questions

_(vazio)_
