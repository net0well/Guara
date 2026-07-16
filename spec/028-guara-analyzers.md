# Spec 028: `Guara.Analyzers` — Analisadores Roslyn

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Analyzers`
**Depende de:** — (analisadores de compilação; sem dependência de runtime)
**Docs de referência:** [dependency-rules](../docs/dependency-rules.md) · [anti-patterns](../docs/anti-patterns.md) · [checklist](../docs/checklist.md)

## Problem

As regras arquiteturais do Guará (dependências unidirecionais, só-contratos, um `AddGuara` por pacote, zero reflection) valem tanto para o próprio framework quanto para quem o **estende** (providers de terceiros). Documentação não basta — precisam ser **enforçadas em tempo de compilação**. `Guara.Analyzers` transforma os anti-padrões em **erros/avisos de build** com diagnósticos `GUARA*`.

## Scope

### In

- Diagnósticos que enforçam [dependency-rules](../docs/dependency-rules.md) e [anti-patterns](../docs/anti-patterns.md):
  - `GUARA0001` — dependência invertida (arestas "de baixo para cima").
  - `GUARA0002` — referência a implementação concreta de outro componente (deveria ser contrato).
  - `GUARA0003` — mais de um ponto de entrada público (`AddGuara.../Use...`) por pacote.
  - `GUARA0004` — reflection dinâmica em runtime (deveria ser source gen).
  - `GUARA0005` — `.Result`/`.Wait()`/`Thread.Sleep` no runtime.
  - `GUARA0006` — factory global estática / singleton estático mutável.
  - `GUARA0007` — extensão de DI fora do namespace `Microsoft.Extensions.DependencyInjection`.
- **Code fixes** onde fizer sentido (ex.: mover extensão para o namespace correto).

### Out

- Regras em runtime (analisador só age em compilação).
- Análise de projetos do usuário que não usam o Guará (opt-in por referência ao pacote).

## Domain Model

- Cada regra = um `DiagnosticAnalyzer` com id `GUARA000N`, severidade e mensagem localizável.
- Categoria `Guara.Architecture`; documentação de cada diagnóstico com link para os docs.

## API Contract

Não há API .NET pública convencional; o "contrato" é o **conjunto de diagnósticos** (`GUARA0001..N`), suas severidades default e os code fixes.

| Id | Severidade default | Regra |
|---|---|---|
| GUARA0001 | Error | Dependência invertida |
| GUARA0002 | Error | Referência a implementação concreta |
| GUARA0003 | Warning | >1 ponto de entrada por pacote |
| GUARA0004 | Warning | Reflection dinâmica em runtime |
| GUARA0005 | Warning | Bloqueio de thread |
| GUARA0006 | Error | Estático global mutável |
| GUARA0007 | Warning | Namespace de extensão incorreto |

## Authorization

N/A.

## Edge Cases & Failure Modes

- **Falso positivo** → severidade configurável via `.editorconfig`; cada regra suprimível com justificativa.
- **Performance do analisador** → incremental, sem varrer a solução inteira a cada tecla (skill `roslyn-incremental-generator-specialist`).
- **Código de terceiros** → regras só aplicam a assemblies que referenciam o Guará.

## Non-Functional Requirements

- Analisadores **incrementais** e rápidos (não degradam a IDE).
- Mensagens acionáveis com link para o doc correspondente.
- Cobertos por testes (`Microsoft.CodeAnalysis.Testing`).

## Integrations

Distribuído junto dos pacotes do Guará (analisador transitivo); valida também extensões de terceiros. Complementa as ferramentas MCP `cwm-roslyn-navigator` usadas em revisão.

## Acceptance Criteria

- **AC-1 — GUARA0001.** *Dado* `Guara.Abstractions` referenciando `Guara.Core`, *então* erro de build `GUARA0001`.
- **AC-2 — GUARA0002.** *Dado* um motor referenciando `SqlServerStorage` concreto, *então* erro `GUARA0002`.
- **AC-3 — GUARA0003.** *Dado* dois métodos `AddGuara*` públicos num pacote, *então* aviso `GUARA0003`.
- **AC-4 — GUARA0004.** *Dado* `Activator.CreateInstance` no hot path, *então* aviso `GUARA0004`.
- **AC-5 — GUARA0007 + fix.** *Dado* uma extensão fora de `Microsoft.Extensions.DependencyInjection`, *então* aviso `GUARA0007` com code fix que corrige o namespace.
- **AC-6 — Supressão.** *Dado* uma supressão justificada em `.editorconfig`, *então* o diagnóstico é silenciado.
- **AC-7 — Incremental.** *Dado* edição num arquivo grande, *então* o analisador não trava a IDE.

## Deferred Decisions

- **DD-1 — Severidades default.** *Fallback:* conforme tabela acima; ajustável por feedback. *Revisão:* pós-MVP.
- **DD-2 — Conjunto inicial de regras.** *Fallback:* GUARA0001–0002 (as que quebram build) no MVP; demais em seguida. *Revisão:* pós-MVP.

## Open Questions

_(vazio)_
