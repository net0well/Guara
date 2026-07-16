# Spec 034: CI/CD & Release (Publicação no NuGet)

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Escopo:** pipeline (GitHub Actions)
**Depende de:** [Spec 033](033-empacotamento-build-versionamento.md), specs de storage (011–015)
**Docs de referência:** [performance](../docs/performance.md) · [ADR-0008](../docs/adr/0008-native-aot-e-trimming.md)

## Problem

Um projeto open-source confiável precisa de **CI reproduzível** e **release automatizado**: cada PR é validado (build multi-TFM, testes, AOT, conformance de providers), e cada tag publica pacotes assinados no NuGet.org com símbolos. Sem isso, qualidade e cadência de release viram trabalho manual e frágil.

## Scope

### In

- **CI (PR/push)**: restore (CPM) → build `net8`+`net10` → testes unit/integração → **matriz AOT** (`net10`, `PublishAot`) → **conformance de storage** via **Testcontainers** (Postgres/SqlServer/Redis/Mongo) → analyzers `GUARA*` → API-compat.
- **CD (tag `v*`)**: `dotnet pack` (core + símbolos) → assinar → `dotnet nuget push` para NuGet.org + servidor de símbolos → criar GitHub Release com notas.
- **Container** do `Guara.Host` de exemplo (publish SDK container / chiseled) para deploy (Portainer/VPS).
- Cache de dependências; execução paralela por TFM/provider.

### Out

- Deploy da app do usuário (responsabilidade dele).
- Publicação dos pacotes **Pro** no NuGet público (fluxo comercial à parte — Spec 035).

## Domain Model

- Workflows: `ci.yml` (PR/push), `release.yml` (tags), `codeql.yml` (análise de segurança).
- Segredos do pipeline: `NUGET_API_KEY`, chave de assinatura, credenciais de container registry — via secrets do repositório (nunca no código).
- Matriz: `{ tfm: [net8.0, net10.0] } × { provider: [postgres, sqlserver, redis, mongo] }` (conformance).

## API Contract

O "contrato" são os **gatilhos e gates** dos workflows (build verde obrigatório para merge; tag dispara publish).

## Authorization

Publicação restrita a maintainers (branch protection + environments protegidos no GitHub). Segredos só em jobs de release.

## Edge Cases & Failure Modes

- **Conformance falha em 1 provider** → release bloqueado (não publica pacote quebrado).
- **AOT warning novo** → CI vermelho.
- **Push duplicado da mesma versão** → idempotente/skip (não republica).
- **Testcontainers indisponível** (sem Docker no runner) → job marcado e falha explícita (não “verde falso”).
- **Símbolos ausentes** → gate de release falha.

## Non-Functional Requirements

- CI reproduzível e cacheado; feedback de PR em tempo razoável (paralelismo).
- Publicação **atômica por versão**; rollback = nova versão (nunca "unlist" silencioso como fix).
- Cobertura reportada; benchmarks executáveis (comparação opcional de regressão).

## Integrations

GitHub Actions; NuGet.org; Testcontainers (Docker); container registry; opcional Codecov.

## Acceptance Criteria

- **AC-1 — CI multi-TFM.** *Dado* um PR, *então* build+testes rodam para `net8.0` e `net10.0` e precisam passar para merge.
- **AC-2 — AOT gate.** *Dado* um PR que introduz warning de trim/AOT, *então* o CI falha.
- **AC-3 — Conformance.** *Dado* o pipeline, *então* cada provider roda o conformance kit (Spec 004) via Testcontainers e precisa passar.
- **AC-4 — Release por tag.** *Dada* a tag `vX.Y.Z`, *então* os pacotes OSS + `.snupkg` são publicados no NuGet.org com a versão X.Y.Z.
- **AC-5 — Assinatura.** *Dado* um pacote publicado, *então* ele está assinado e o SourceLink resolve.
- **AC-6 — Pro isolado.** *Dado* o release público, *então* pacotes `Guara.Pro.*` **não** vão para o NuGet público.
- **AC-7 — Segurança.** *Dado* o pipeline, *então* CodeQL/scan roda e segredos nunca aparecem em logs.

## Deferred Decisions

- **DD-1 — Registry dos pacotes Pro.** *Fallback:* feed privado/loja própria (Spec 035). *Revisão:* Spec 035.
- **DD-2 — Benchmark de regressão como gate.** *Fallback:* informativo no 1.0; gate depois de estabilizar baselines. *Revisão:* pós-1.0.
- **DD-3 — Provider de CI.** *Fallback:* GitHub Actions. *Revisão:* nenhuma.

## Open Questions

_(vazio)_
