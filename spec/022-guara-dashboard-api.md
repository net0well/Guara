# Spec 022: `Guara.Dashboard.Api` — APIs do Dashboard

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Dashboard.Api`
**Depende de:** [Spec 001](001-guara-abstractions.md), [Spec 004](004-guara-storage.md), [Spec 020](020-guara-authentication.md), [Spec 021](021-guara-authorization.md)
**Docs de referência:** [components](../docs/components.md) · [anti-patterns](../docs/anti-patterns.md)

> **Atualização 2026-07-16:** tempo real (SSE) promovido de *Deferred* para **escopo do MVP**, a pedido do produto ("o painel precisa ser em tempo real").

## Problem

O dashboard precisa de dados: filas, jobs (por estado), detalhes, servidores/nós, métricas agregadas — e ações (disparar, retentar, excluir). `Guara.Dashboard.Api` expõe essas APIs HTTP de forma **paginada, autorizada e versionada**. Regra dura: **nunca renderiza HTML** — só entrega JSON. A UI é a Spec 024.

## Scope

### In

- Endpoints (Minimal API) de **leitura**: filas, jobs por estado (paginado), detalhe do job, servidores/nós, contadores/métricas agregadas.
- Endpoints de **ação**: enfileirar, retentar, excluir, disparar recurring (protegidos por policy da Spec 021).
- **OpenAPI** documentado; versionamento (`/api/v1`).
- Paginação e limites obrigatórios em toda listagem.
- **Tempo real** via Server-Sent Events: `GET /api/v1/stream` transmite mudanças (estado de job, contadores, nós) ao dashboard.
- Endpoints avançados (busca/filtros, séries p/ gráficos, ações em massa, gestão de recorrentes e **de calendários**) → [Spec 032](032-dashboard-avancado.md).

### Out

- **Renderizar HTML/SPA** → é a Spec 024.
- Acesso direto a provider concreto (usa `IJobStorage`/`IGuaraClient`).

## Domain Model

- Recursos: `queues`, `jobs`, `jobs/{id}`, `servers`, `stats`, `recurring`.
- O detalhe do job (`jobs/{id}`) inclui a **linha do tempo de estados** quando o `StateHistory` estiver habilitado ([Spec 004](004-guara-storage.md)); sem ele, exibe apenas o estado atual.
- DTOs planos (sem vazar entidades internas); paginação `?page&pageSize` com teto.
- Ações mapeadas às permissões `guara:*` (Spec 021).

## API Contract

| Verbo | Rota | Ação | Autorização |
|---|---|---|---|
| GET | `/api/v1/stats` | contadores por estado/fila | `guara:view` |
| GET | `/api/v1/queues` | lista filas | `guara:view` |
| GET | `/api/v1/jobs?state=&queue=&page=&pageSize=` | lista paginada | `guara:view` |
| GET | `/api/v1/jobs/{id}` | detalhe (payload sob `guara:view-payload`) | `guara:view` |
| GET | `/api/v1/servers` | nós/heartbeat | `guara:view` |
| POST | `/api/v1/jobs/{id}/retry` | retentar | `guara:retry` |
| POST | `/api/v1/jobs/{id}/trigger` | disparar agora | `guara:trigger` |
| DELETE | `/api/v1/jobs/{id}` | excluir | `guara:delete` |
| GET | `/api/v1/stream` | **stream SSE** de eventos (estado/contadores/nós) | `guara:view` |

Respostas com `ProblemDetails` (RFC 9457) em erro; sucesso com DTO + envelope de paginação.

## Authorization

Todo endpoint exige autenticação (Spec 020) e a policy correspondente (Spec 021). **Deny by default**. `view-payload` é ação separada (dados sensíveis).

## Edge Cases & Failure Modes

- **Listagem sem paginação** → proibido; `pageSize` tem teto (ex.: 100).
- **Job inexistente** → 404 `ProblemDetails`.
- **Ação sem permissão** → 403.
- **Storage sem consulta rica** (ex.: Redis) → endpoints degradam conforme `Capabilities` (Spec 004), documentado.
- **Payload sensível** → só com `guara:view-payload`; caso contrário, ocultado.
- **SSE (`/api/v1/stream`)** → autenticado; reconexão automática via `Last-Event-ID`; keep-alive periódico; **exige `proxy_buffering off`** no proxy reverso (ver `Infra/nginx`).

## Non-Functional Requirements

- Minimal API (`dotnet-claude-kit:minimal-api`), OpenAPI (`:openapi`/`:scalar`), versionada (`:api-versioning`).
- Consultas **paginadas e limitadas**, sem N+1 (`dotnet-skills:database-performance`).
- Sem HTML; CORS restrito; rate limiting nas ações mutáveis.
- Stream SSE de baixo overhead (server→cliente), com heartbeat/keep-alive; alimentado pelo push do storage quando disponível.

## Integrations

Lê via `IJobStorage` (Spec 004); age via `IGuaraClient` (Spec 005); protegida por Specs 020/021; consumida pela SPA (Spec 024).

## Acceptance Criteria

- **AC-1 — Só JSON.** *Dado* qualquer endpoint, *então* retorna JSON/`ProblemDetails` — nunca HTML.
- **AC-2 — Paginação obrigatória.** *Dado* `GET /jobs`, *então* respeita `page/pageSize` com teto; sem retorno ilimitado.
- **AC-3 — Autorização.** *Dado* usuário sem `guara:delete`, *quando* `DELETE /jobs/{id}`, *então* 403.
- **AC-4 — Payload protegido.** *Dado* falta de `guara:view-payload`, *então* o detalhe omite o payload.
- **AC-5 — OpenAPI.** *Dado* o app, *então* há documento OpenAPI válido e versionado.
- **AC-6 — Ações funcionam.** *Dado* `POST /jobs/{id}/retry` autorizado, *então* o job é reenfileirado.
- **AC-7 — Degradação honesta.** *Dado* provider sem consulta rica, *então* os endpoints afetados degradam conforme `Capabilities` (documentado), sem erro 500.
- **AC-8 — Stream em tempo real.** *Dado* um cliente conectado a `/api/v1/stream`, *quando* um job muda de estado, *então* o cliente recebe um evento SSE em ~1s sem recarregar a página.

## Deferred Decisions

- **DD-1 — Tempo real (resolvido).** *Decisão:* **SSE no MVP** (`/api/v1/stream`), server→cliente, compatível com nginx/proxy e com reconexão automática; WebSocket/SignalR só se surgir necessidade bidirecional. *Multi-nó:* o stream é alimentado pelo push do storage (LISTEN/NOTIFY, keyspace notifications, change streams) quando `Capabilities` suportar, com fallback para reconciliação por polling curto no servidor — funciona em cluster sem exigir o bridge distribuído (Spec 026).
- **DD-2 — Teto de `pageSize`.** *Fallback:* 100. *Revisão:* feedback.
- **DD-3 — Rate limiting default.** *Fallback:* limite conservador nas ações mutáveis. *Revisão:* Spec 020.

## Open Questions

_(vazio)_
