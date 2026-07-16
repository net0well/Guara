# Spec 020: `Guara.Authentication` — Autenticação

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Authentication`
**Depende de:** [Spec 001](001-guara-abstractions.md)
**Docs de referência:** [components](../docs/components.md) · [checklist](../docs/checklist.md)

## Problem

O Dashboard e as APIs administrativas do Guará **não podem ser abertos** — expor um scheduler sem autenticação é um risco sério, ainda mais num projeto open-source amplamente instalado. `Guara.Authentication` define **quem é o chamador** do Dashboard/API, reutilizando os esquemas padrão do ASP.NET Core (cookies, JWT/OIDC), sem inventar identidade própria.

## Scope

### In

- Contrato/ponto de extensão para autenticar requisições do `Guara.Dashboard.Api` (Spec 022).
- Integração com esquemas do ASP.NET Core: **cookie**, **JWT bearer**, **OIDC**.
- Modo **seguro por padrão**: dashboard exige autenticação a menos que explicitamente liberado.

### Out

- Decisão de **o que** cada identidade pode fazer → `Guara.Authorization` (Spec 021).
- Identidade dos jobs em si (jobs não têm login; rodam sob o processo).
- Provedor de usuários próprio (reutiliza o do host).

## Domain Model

- **`GuaraAuthenticationOptions`** — esquema(s) aceitos, se anônimo é permitido (default: não).
- Reutiliza `ClaimsPrincipal` do ASP.NET Core; sem store de usuários próprio.
- Ponto de extensão para o host plugar seu esquema existente.

## API Contract

```csharp
namespace Microsoft.Extensions.DependencyInjection;
public static class GuaraAuthenticationExtensions
{
    public static IGuaraBuilder AddGuaraAuthentication(this IGuaraBuilder builder,
        Action<GuaraAuthenticationOptions>? configure = null);
}

public sealed class GuaraAuthenticationOptions
{
    public string[] Schemes { get; set; } = []; // vazio => usa o default do host
    public bool AllowAnonymousDashboard { get; set; } = false; // seguro por padrão
}
```

## Authorization

Este pacote **autentica** (quem é); **autorizar** (o que pode) é da Spec 021. Ambos compõem o `AuthorizationMiddleware`/pipeline do Dashboard.

## Edge Cases & Failure Modes

- **Nenhum esquema configurado + dashboard exposto** → aviso forte no startup; anônimo negado por padrão.
- **Token expirado/inválido** → 401 no Dashboard.Api.
- **Ambiente de dev** → liberar anônimo é opt-in explícito (`AllowAnonymousDashboard=true`), nunca default.
- **Sem HTTPS** → aviso (cookies/JWT exigem transporte seguro).

## Non-Functional Requirements

- **Seguro por padrão** (deny by default).
- Reutiliza ASP.NET Core Authentication (sem cripto/identidade caseira — skill `dotnet-claude-kit:authentication`).
- AOT/Trimming conforme suporte do ASP.NET Core.

## Integrations

ASP.NET Core Authentication; consumido por `Guara.Dashboard.Api` (Spec 022).

## Acceptance Criteria

- **AC-1 — Deny by default.** *Dado* dashboard sem config de auth, *então* requisições anônimas são negadas (401) e há aviso no startup.
- **AC-2 — JWT.** *Dado* um bearer válido no esquema configurado, *então* a requisição é autenticada com o `ClaimsPrincipal` correto.
- **AC-3 — Cookie/OIDC.** *Dado* o esquema do host, *então* o Guará reaproveita a identidade sem store próprio.
- **AC-4 — Anônimo opt-in.** *Dado* `AllowAnonymousDashboard=true`, *então* (e só então) o dashboard aceita anônimo.
- **AC-5 — Token inválido.** *Dado* token expirado, *então* 401.

## Deferred Decisions

- **DD-1 — API key para automação.** *Fallback:* suportar API key como esquema opcional além de JWT/cookie. *Revisão:* pós-MVP.
- **DD-2 — Esquema default.** *Fallback:* usar o default do host quando `Schemes` vazio. *Revisão:* nenhuma.

## Open Questions

_(vazio)_
