# Spec 027: `Guara.Cli` — Ferramenta de Linha de Comando

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Cli`
**Depende de:** [Spec 001](001-guara-abstractions.md), [Spec 004](004-guara-storage.md), [Spec 009](009-guara-hosting.md)
**Docs de referência:** [components](../docs/components.md) · [naming-conventions](../docs/naming-conventions.md)

## Problem

Operadores e desenvolvedores precisam interagir com o Guará fora do app: aplicar migrations de storage, inspecionar/enfileirar/retentar jobs, verificar saúde do cluster, purgar. Uma **CLI** (`dotnet tool`) dá acesso administrativo scriptável em CI/CD e produção. Sendo tooling (fora do runtime crítico), pode relaxar a regra AOT (ver CLAUDE.md).

## Scope

### In

- `dotnet tool` global/local `guara`.
- Comandos: `migrate` (aplica esquema do storage), `jobs list|get|retry|delete`, `recurring list|trigger`, `servers list`, `purge`, `doctor` (diagnóstico de config/conexão).
- Saída legível e **`--json`** para automação; exit codes significativos.

### Out

- Executar jobs (isso é do servidor); a CLI **comanda**, não processa.
- UI (é o Dashboard).

## Domain Model

- Cada comando resolve o storage/coordenador via a mesma configuração do app (Spec 018) e atua por contratos (`IJobStorage`, `IGuaraClient`, `IClusterCoordinator`).
- Sem lógica de negócio própria — orquestra os contratos.

## API Contract (CLI)

```
guara migrate --provider postgres --connection "..."
guara jobs list --state failed --queue default --json
guara jobs retry <id>
guara recurring trigger <id>
guara servers list
guara purge --older-than 7d --state succeeded
guara doctor
```

## Authorization

Acesso administrativo pressupõe acesso ao storage/segredos (mesma confiança de um DBA). Em cenários protegidos, a CLI usa credenciais da config; nunca embute segredos.

## Edge Cases & Failure Modes

- **Config/conexão inválida** → `doctor` reporta claramente; comandos falham com exit code ≠ 0 e mensagem acionável.
- **Comando destrutivo** (`purge`, `delete`) → confirmação interativa, exceto com `--yes` (para CI).
- **Provider sem recurso** (ex.: consulta limitada) → degrada conforme `Capabilities`, informando o usuário.
- **Versão de esquema divergente** → `migrate` detecta e orienta.

## Non-Functional Requirements

- Scriptável (`--json`, exit codes); mensagens claras.
- Distribuída como `dotnet tool` (skill `dotnet-skills:local-tools`).
- Não precisa ser AOT (tooling), mas deve iniciar rápido.

## Integrations

Usa os contratos do framework (Spec 004/005/025) e a configuração (Spec 018); útil em pipelines de CI/CD (skill `dotnet-claude-kit:ci-cd`).

## Acceptance Criteria

- **AC-1 — Migrate.** *Dado* `guara migrate` com provider/conn válidos, *então* o esquema é aplicado idempotentemente.
- **AC-2 — Jobs.** *Dado* `guara jobs list --state failed --json`, *então* retorna JSON parseável com os jobs falhos.
- **AC-3 — Retry.** *Dado* `guara jobs retry <id>`, *então* o job é reenfileirado.
- **AC-4 — Destrutivo protegido.** *Dado* `guara purge` sem `--yes`, *então* pede confirmação; com `--yes`, executa sem prompt.
- **AC-5 — Doctor.** *Dado* config inválida, *então* `guara doctor` aponta o problema e retorna exit code ≠ 0.
- **AC-6 — Exit codes.** *Dado* qualquer falha, *então* o exit code é ≠ 0 (scriptável).

## Deferred Decisions

- **DD-1 — Framework de CLI.** *Fallback:* `System.CommandLine`. *Revisão:* implementação.
- **DD-2 — Escopo do MVP.** *Fallback:* `migrate`, `jobs`, `doctor` no MVP; `recurring/servers/purge` na sequência. *Revisão:* feedback.

## Open Questions

_(vazio)_
