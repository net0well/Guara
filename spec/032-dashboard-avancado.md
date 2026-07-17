# Spec 032: Dashboard Avançado — Busca, Filtros, Gráficos ao Vivo, Ações em Massa

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Escopo:** feature — estende [Spec 022 (`Dashboard.Api`)](022-guara-dashboard-api.md) e [Spec 024 (`Dashboard.Angular`)](024-guara-dashboard-angular.md)
**Licença:** OSS (core)
**Depende de:** [Spec 022](022-guara-dashboard-api.md), [Spec 024](024-guara-dashboard-angular.md), [Spec 004](004-guara-storage.md)

## Problem

Um dashboard "completo como o do Hangfire" precisa de mais que listas: **busca e filtros ricos**, **gráficos ao vivo** (throughput, sucesso/falha, latência), **gestão de recorrentes** (pausar/disparar/editar) e **ações em massa** (re-enfileirar/excluir seleção). Isto eleva o dashboard de "observável" para "operável".

## Scope

### In

- **Busca/filtros**: por tipo de job, fila, estado, intervalo de tempo, texto (id/nome), tags.
- **Gráficos ao vivo**: throughput, taxa de sucesso/falha, latência (p50/p95), tamanho de fila — alimentados pelo stream SSE (Spec 022) + métricas (Spec 016).
- **Gestão de recorrentes**: listar, pausar/retomar, disparar agora, editar cron.
- **Gestão de calendários** ([Spec 038](038-agendamento-fluente.md)) — criar, editar e excluir calendários **pelo painel**: adicionar feriados, excluir datas/intervalos/dias da semana, com uma interface **leve e agradável** (visão mensal clicável + lista de exclusões). Mostra quais recorrentes usam cada calendário e uma prévia das próximas ocorrências afetadas. Usa o **mesmo caminho de persistência** do `AdicionarOuAtualizarCalendarioAsync` (estrutura `Calendars` via `IRecurringStorage`) — portanto os recorrentes **recalculam automaticamente** (updateTriggers), independentemente de a mudança vir do código ou do painel.
- **Ações em massa**: selecionar N jobs → re-enfileirar/excluir, com confirmação.
- **Visão de servidores/nós** e de **continuations/batches** (status agregado).

### Out

- Autenticação/autorização (herda Specs 020/021 — cada ação exige sua permissão).
- Multi-tenancy (fora do 1.0).

## Domain Model

- Endpoints de consulta com filtros compostos e paginação (estende a tabela da Spec 022).
- Séries temporais agregadas para os gráficos (do storage/métricas), com janelas (1h/24h/7d).
- Ações em massa operam sobre um conjunto de `JobId` validado por permissão.

## API Contract (adições à Spec 022)

| Verbo | Rota | Ação | Autorização |
|---|---|---|---|
| GET | `/api/v1/jobs/search?q=&type=&queue=&state=&from=&to=&page=&pageSize=` | busca/filtros | `guara:view` |
| GET | `/api/v1/stats/series?metric=&window=` | série temporal p/ gráficos | `guara:view` |
| GET/POST | `/api/v1/recurring` · `/recurring/{id}/pause|resume|trigger` | gestão de recorrentes | `guara:trigger` |
| GET | `/api/v1/calendars` | lista calendários | `guara:view` |
| GET | `/api/v1/calendars/{nome}` | detalhe: exclusões + recorrentes que o usam + prévia de ocorrências | `guara:view` |
| PUT | `/api/v1/calendars/{nome}` | cria/atualiza (upsert — espelho do `AdicionarOuAtualizarCalendarioAsync`) | `guara:calendars` |
| DELETE | `/api/v1/calendars/{nome}` | exclui; **bloqueado se em uso** → 409 com a lista de recorrentes | `guara:calendars` |
| POST | `/api/v1/jobs/bulk/retry` · `/bulk/delete` | ações em massa | `guara:retry`/`guara:delete` |

## Authorization

Toda ação mapeada à permissão correspondente (Spec 021); ações em massa validam permissão para **cada** item. Deny-by-default.

## Edge Cases & Failure Modes

- **Busca cara** → filtros exigem índices adequados no provider; sempre paginada com teto; degrada conforme `Capabilities` (Redis tem busca limitada — documentado).
- **Ação em massa parcial** → relata sucesso/falha por item; nunca "tudo ou nada" silencioso.
- **Gráficos sem dados** → estados vazios claros.
- **Séries de alta cardinalidade** → agregação server-side com janelas fixas (sem explodir memória).
- **Concorrência** → editar recorrente enquanto dispara é seguro (idempotente).
- **Calendário editado ao mesmo tempo por código e painel** → upsert, última escrita vence (mesma semântica do `AdicionarOuAtualizarCalendarioAsync`); o recálculo de ocorrências roda após qualquer escrita.
- **Excluir calendário em uso pelo painel** → 409 listando os recorrentes (mesma regra da Spec 038 — sem órfãos).

## Non-Functional Requirements

- Front: virtualização de listas, lazy loading por rota, gráficos leves/acessíveis (skills `angular-developer` e `dataviz`).
- API: consultas indexadas, sem N+1, paginação obrigatória; agregações server-side.
- Tempo real via SSE (Spec 022) para atualizar gráficos/contadores sem refresh.

## Integrations

Estende Dashboard.Api (Spec 022) e SPA (Spec 024); consome métricas (Spec 016) e status de continuations/batches (Specs 030/031).

## Acceptance Criteria

- **AC-1 — Busca.** *Dado* filtros por tipo+fila+estado+intervalo, *então* a API retorna resultados paginados corretos.
- **AC-2 — Gráficos ao vivo.** *Dado* jobs sendo processados, *então* os gráficos atualizam via SSE em ~1s.
- **AC-3 — Recorrentes.** *Dado* um recurring, *quando* pauso, *então* ele não dispara até retomar; disparo manual funciona. *Quando* retomo, *então* **sem backfill** — nada do período pausado roda; a próxima ocorrência é a próxima válida ([semantics](../docs/semantics.md)).
- **AC-4 — Ações em massa.** *Dado* 50 jobs selecionados, *quando* re-enfileiro, *então* todos os autorizados são re-enfileirados e o resultado por item é reportado.
- **AC-5 — Permissão em massa.** *Dado* usuário sem `guara:delete`, *então* a ação de exclusão em massa é negada.
- **AC-6 — Degradação.** *Dado* provider com busca limitada, *então* a UI oferece os filtros suportados e informa os indisponíveis (sem erro 500).
- **AC-7 — Calendário pelo painel é respeitado.** *Dado* um feriado adicionado a um calendário pela UI, *então* os recorrentes que o usam têm o `NextRun` recalculado e **pulam a data** — mesmo efeito do `AdicionarOuAtualizarCalendarioAsync` por código.
- **AC-8 — Exclusão protegida.** *Dado* um calendário em uso, *quando* excluído pela UI, *então* a API responde 409 com a lista de recorrentes e nada é removido.
- **AC-9 — Permissão de calendários.** *Dado* usuário sem `guara:calendars`, *então* criar/editar/excluir calendários é negado (403); visualizar exige apenas `guara:view`.

## Deferred Decisions

- **DD-1 — Janelas de série temporal.** *Fallback:* 1h/24h/7d. *Revisão:* feedback.
- **DD-2 — Persistência de métricas agregadas.** *Fallback:* agregar do storage/OTel; tabela de rollup opcional se performance exigir. *Revisão:* benchmarks.

## Open Questions

_(vazio)_
