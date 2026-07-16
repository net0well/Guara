# Spec 018: `Guara.Configuration` — Configuração e Options

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Configuration`
**Depende de:** [Spec 001](001-guara-abstractions.md)
**Docs de referência:** [naming-conventions](../docs/naming-conventions.md) · [checklist](../docs/checklist.md)

## Problem

Cada componente tem seu `*Options` ([checklist](../docs/checklist.md) exige). É preciso um jeito **consistente e validado** de bindar configuração (appsettings/env/secrets) para essas opções, falhando **cedo** quando algo estiver errado — em vez de descobrir em runtime. `Guara.Configuration` centraliza o padrão Options do Guará.

## Scope

### In

- Padrão de binding das `*Options` (`IConfiguration` → `SchedulerOptions`, `WorkerOptions`, etc.) sob a seção `Guara`.
- **Validação no startup** via `IValidateOptions`/DataAnnotations (`ValidateOnStart`).
- Convenção de seções (`Guara:Scheduler`, `Guara:Worker`, `Guara:Storage`, ...).
- Suporte a **secrets** (connection strings) sem logá-los.

### Out

- Definição das próprias `*Options` (cada componente define a sua).
- Providers de configuração (é do BCL/host).

## Domain Model

- Convenção de seção → tipo: `Guara:{Componente}` liga em `{Componente}Options`.
- Validadores registrados por componente; `ValidateOnStart()` garante falha no boot.
- `IOptionsSnapshot` para opções que podem mudar; `IOptions` para imutáveis.

## API Contract

```csharp
namespace Microsoft.Extensions.DependencyInjection;

public static class GuaraConfigurationExtensions
{
    // usado internamente pelos Add* dos componentes
    public static IGuaraBuilder BindOptions<TOptions>(this IGuaraBuilder builder, string sectionName)
        where TOptions : class;
}
```

## Authorization

N/A. **Segredos** (connection strings, tokens) nunca são logados; recomenda-se `Secret Manager`/env/Key Vault (skill `dotnet-claude-kit:configuration`).

## Edge Cases & Failure Modes

- **Configuração inválida** → falha no startup com o nome da propriedade e o motivo.
- **Seção ausente** → usa defaults do `*Options` (documentado).
- **Segredo em texto claro no appsettings** → aviso em dev; recomendação de secret store.
- **Mudança em runtime** → só via `IOptionsSnapshot`/`IOptionsMonitor`; opções críticas de startup são imutáveis.

## Non-Functional Requirements

- Falha antecipada (`ValidateOnStart`) — sem erro tardio de config.
- Sem reflection custom (usa o binder do BCL); AOT: usa o source generator de Options quando disponível.
- Segredos fora de logs.

## Integrations

`Microsoft.Extensions.Configuration`/`Options`; consumido pelos `Add*` de cada componente (Spec 009).

## Acceptance Criteria

- **AC-1 — Binding por convenção.** *Dado* `Guara:Worker:MaxConcurrency=8`, *então* `WorkerOptions.MaxConcurrency == 8`.
- **AC-2 — Validação no startup.** *Dado* `MaxConcurrency=0` (inválido), *quando* o host inicia, *então* falha com mensagem clara antes de processar jobs.
- **AC-3 — Defaults.** *Dado* seção ausente, *então* os defaults do `*Options` são usados sem erro.
- **AC-4 — Segredos não logados.** *Dado* uma connection string, *então* ela não aparece em logs.
- **AC-5 — Reload.** *Dado* `IOptionsSnapshot`, *então* mudanças de config não-críticas são refletidas sem reiniciar.
- **AC-6 — AOT.** *Dado* `PublishAot=true`, *então* o binding funciona (source gen de Options).

## Deferred Decisions

- **DD-1 — Nome da seção raiz.** *Fallback:* `Guara`. *Revisão:* nenhuma.
- **DD-2 — Estratégia de secrets recomendada.** *Fallback:* env vars/Secret Manager em dev, Key Vault/secret store em prod (documentado). *Revisão:* Spec 020.

## Open Questions

_(vazio)_
