# Spec 024: `Guara.Dashboard.React` — SPA do Dashboard

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Dashboard.React`
**Depende de:** [Spec 022 (`Guara.Dashboard.Api`)](022-guara-dashboard-api.md) — **somente** via contrato HTTP
**Skills obrigatórias (front):** `agent-skills/skills/react-best-practices` · `.../composition-patterns` · `.../web-design-guidelines` · `.../react-view-transitions`

> **Atualização 2026-07-16:** atualização em **tempo real** promovida de *Deferred* para **escopo do MVP** — o painel reflete mudanças ao vivo via SSE.

## Problem

Operadores precisam de uma interface para **observar e agir** sobre os jobs: ver filas, jobs por estado, detalhes, retentar/excluir, acompanhar servidores. A SPA React consome **apenas** a API (Spec 022), sem qualquer conhecimento interno do framework — trocar a API ou a UI não deve afetar o outro lado. Sendo open-source, a UI precisa ser acessível, performática e agradável em instalações variadas.

## Scope

### In

- SPA React (TypeScript) consumindo a API v1 (Spec 022).
- Telas: **Visão geral** (contadores/gráficos), **Jobs** (lista paginada + filtros por estado/fila), **Detalhe do job** (com payload sob permissão), **Recorrentes**, **Servidores/Nós**.
- Ações: retentar, disparar, excluir (conforme permissões retornadas pela API).
- **Atualização em tempo real** via SSE (`/api/v1/stream`): contadores, listas e detalhe refletem mudanças sem refresh (com fallback para polling).
- Telas avançadas (busca/filtros, gráficos ao vivo, gestão de recorrentes, ações em massa) → [Spec 032](032-dashboard-avancado.md).
- Tema claro/escuro, i18n (pt-BR/en), acessibilidade (WCAG).
- Build que gera **assets estáticos** embutidos por `Guara.Dashboard` (Spec 023).

### Out

- Qualquer lógica de negócio/scheduler (fica no backend).
- Acesso a storage ou conhecimento de providers.
- Autenticação própria (usa a sessão/esquema exposto pela API — Spec 020).

## Domain Model (client-side)

- Camada de **data fetching** tipada sobre a API (React Query/TanStack ou fetch + cache), com invalidação após ações.
- Componentes por **composição** (compound components; sem proliferação de props booleanas) — skill `composition-patterns`.
- Estado do servidor separado do estado de UI; sem estado global desnecessário.

## API Contract

Consome os endpoints da [Spec 022](022-guara-dashboard-api.md) (`/api/v1/*`). Contrato HTTP é a **única** dependência. Erros exibidos a partir de `ProblemDetails`.

## Authorization

A UI **reflete** as permissões vindas da API (esconde/desabilita ações sem permissão), mas a **decisão real é do backend** (Spec 021) — a UI nunca é a fronteira de segurança. Chamadas 401 → fluxo de login; 403 → feedback claro.

## Edge Cases & Failure Modes

- **API indisponível** → estados de erro/skeleton, retry com back-off; nunca tela branca.
- **Listas grandes** → paginação/virtualização; nunca carregar tudo (skill `react-best-practices`).
- **Permissão ausente** → ação escondida/desabilitada com tooltip explicativo.
- **Payload sensível** → só exibido se a API o retornar (permissão `guara:view-payload`).
- **Provider com consulta limitada** (ex.: Redis) → UI adapta recursos conforme `stats`/`capabilities`.
- **Atualização ao vivo** → **SSE** via `EventSource` (reconexão automática); se o stream cair, fallback automático para polling — nunca fica desatualizado silenciosamente.

## Non-Functional Requirements

- **Performance** (skill `react-best-practices`): code-splitting por rota, memoização criteriosa, evitar re-renders, bundle enxuto, data fetching com cache/dedupe.
- **Composição** (skill `composition-patterns`): APIs de componente reutilizáveis, sem "boolean props" proliferando.
- **UX/Acessibilidade** (skill `web-design-guidelines`): navegação por teclado, contraste, ARIA, foco visível, responsivo.
- **Transições** (skill `react-view-transitions`): navegação entre telas com View Transitions, sem libs pesadas.
- **i18n** pt-BR/en; tema claro/escuro; SPA sem dependência de servidor de UI (arquivos estáticos).

## Integrations

Somente a API HTTP (Spec 022). Empacotada e servida por `Guara.Dashboard` (Spec 023) como assets embutidos.

## Acceptance Criteria

- **AC-1 — Só API.** *Dado* o código da SPA, *então* toda comunicação com o backend passa pelo contrato HTTP `/api/v1` — sem acoplamento interno.
- **AC-2 — Lista paginada.** *Dado* muitos jobs, *então* a UI pagina/virtualiza; nunca carrega tudo de uma vez.
- **AC-3 — Ações refletem permissão.** *Dado* usuário sem `guara:delete`, *então* a ação Excluir não aparece/está desabilitada.
- **AC-4 — Erros claros.** *Dado* API 4xx/5xx, *então* a UI mostra mensagem a partir de `ProblemDetails`, sem quebrar.
- **AC-5 — Acessibilidade.** *Dado* uma auditoria WCAG básica, *então* navegação por teclado e contraste passam (skill `web-design-guidelines`).
- **AC-6 — Performance.** *Dado* o bundle, *então* há code-splitting por rota e o first load fica dentro de um orçamento razoável.
- **AC-7 — Transições.** *Dado* navegação entre telas, *então* usa View Transitions de forma suave e acessível.
- **AC-8 — Build embutível.** *Dado* o build de produção, *então* gera assets estáticos consumíveis pela Spec 023.
- **AC-9 — Tempo real.** *Dado* o painel aberto, *quando* um job muda de estado no servidor, *então* a UI atualiza em ~1s via SSE, sem refresh manual; se o SSE cair, o polling assume.

## Deferred Decisions

- **DD-1 — Stack de UI/build.** *Fallback:* React + TypeScript + Vite; data fetching com TanStack Query; biblioteca de componentes leve e acessível. *Revisão:* início da implementação da SPA (validar com `react-best-practices`).
- **DD-2 — Tempo real (resolvido).** *Decisão:* **SSE no MVP** consumindo `/api/v1/stream` (Spec 022) via `EventSource`, com reconexão automática e fallback para polling. WebSocket/SignalR só se necessário.
- **DD-3 — Gráficos.** *Fallback:* biblioteca de charts acessível e leve; seguir a skill `dataviz` para paleta/acessibilidade. *Revisão:* implementação.

## Open Questions

_(vazio)_
