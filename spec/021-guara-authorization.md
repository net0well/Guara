# Spec 021: `Guara.Authorization` — Autorização

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Authorization`
**Depende de:** [Spec 001](001-guara-abstractions.md), [Spec 002](002-guara-core.md), [Spec 020](020-guara-authentication.md)
**Docs de referência:** [execution-flows](../docs/execution-flows.md) · [ADR-0007](../docs/adr/0007-pipeline-de-middlewares.md)

## Problem

Autenticar não basta: é preciso decidir **o que** cada identidade pode fazer — ver o dashboard, disparar/retentar/excluir jobs, ver payloads. E, opcionalmente, autorizar a **execução** de certos jobs. `Guara.Authorization` fornece o `AuthorizationMiddleware` (slot do pipeline) e as políticas do Dashboard, reutilizando o modelo de políticas do ASP.NET Core.

## Scope

### In

- **`AuthorizationMiddleware`** (slot Authorization do pipeline, Spec 002) — autoriza a execução de jobs quando aplicável.
- **Políticas do Dashboard**: quem pode `Ver`, `Disparar`, `Retentar`, `Excluir`, `VerPayload`.
- Modelo baseado em **roles/claims/policies** do ASP.NET Core.

### Out

- Autenticar (quem é) → Spec 020.
- Store de permissões próprio (usa claims/policies do host).

## Domain Model

- **Ações do Dashboard**: `guara:view`, `guara:trigger`, `guara:retry`, `guara:delete`, `guara:view-payload`.
- **`GuaraAuthorizationOptions`** — mapeia ações → policies/roles/claims.
- `IJobContext.User` (DD-5 da Spec 001) disponível ao `AuthorizationMiddleware`.

## API Contract

```csharp
namespace Microsoft.Extensions.DependencyInjection;
public static class GuaraAuthorizationExtensions
{
    public static IGuaraBuilder AddGuaraAuthorization(this IGuaraBuilder builder,
        Action<GuaraAuthorizationOptions>? configure = null);
}

public sealed class GuaraAuthorizationOptions
{
    // ação -> policy; por padrão exige um administrador autenticado
    public IDictionary<string,string> ActionPolicies { get; } = new Dictionary<string,string>();
}
```

## Authorization

É o próprio componente de autorização. **Deny by default**: sem política configurada, ações mutáveis (trigger/retry/delete) exigem administrador autenticado.

## Edge Cases & Failure Modes

- **Ação sem policy mapeada** → cai na policy default (admin); nunca "aberto por omissão".
- **Ver payload** → protegido por ação própria (payloads podem conter dados sensíveis).
- **Job não autorizado** → `AuthorizationMiddleware` curto-circuita o pipeline; job vira `Failed`/rejeitado com motivo (não executa).
- **Authorization sem Authentication** → erro de config no startup (autorizar exige autenticar).

## Non-Functional Requirements

- **Deny by default**; menor privilégio.
- Reutiliza `Microsoft.AspNetCore.Authorization` (policies) — sem modelo caseiro.
- Middleware de baixo overhead; não quebra o job (rejeita de forma limpa).

## Integrations

ASP.NET Core Authorization; compõe com Spec 020 (auth) e o pipeline do Core (Spec 002); protege o `Guara.Dashboard.Api` (Spec 022). As **regras de entrada** do dashboard (quem pode acessá-lo) estão na [Spec 037](037-dashboard-autenticacao.md); as permissões desta spec decidem o que se pode **fazer** dentro dele.

## Acceptance Criteria

- **AC-1 — Deny by default.** *Dado* nenhuma policy configurada, *então* `trigger/retry/delete` exigem admin autenticado.
- **AC-2 — Policy por ação.** *Dado* `guara:delete → "SchedulerAdmin"`, *então* só quem tem a policy pode excluir jobs.
- **AC-3 — Payload protegido.** *Dado* falta da ação `guara:view-payload`, *então* o payload não é exibido.
- **AC-4 — Job não autorizado.** *Dado* um job barrado pelo `AuthorizationMiddleware`, *então* ele não executa e é registrado com motivo.
- **AC-5 — Requer auth.** *Dado* `AddGuaraAuthorization` sem `AddGuaraAuthentication`, *então* falha no startup.
- **AC-6 — View só-leitura.** *Dado* um usuário com só `guara:view`, *então* ele vê mas não dispara/exclui.

## Deferred Decisions

- **DD-1 — Autorização de execução de jobs.** *Fallback:* opcional (por tipo de job via atributo/policy); off por padrão. *Revisão:* pós-MVP.
- **DD-2 — Multi-tenant.** *Fallback:* fora do MVP; um escopo administrativo por instância. *Revisão:* pós-MVP se houver demanda.

## Open Questions

_(vazio)_
