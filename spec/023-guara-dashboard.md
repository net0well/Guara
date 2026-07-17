# Spec 023: `Guara.Dashboard` — Composição do Dashboard

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Dashboard`
**Depende de:** [Spec 022 (`Guara.Dashboard.Api`)](022-guara-dashboard-api.md), [Spec 024 (`Guara.Dashboard.Angular`)](024-guara-dashboard-angular.md)
**Docs de referência:** [components](../docs/components.md) · [naming-conventions](../docs/naming-conventions.md)

## Problem

`Guara.Dashboard.Api` entrega JSON e `Guara.Dashboard.Angular` é a SPA. Alguém precisa **compor** os dois numa experiência plug-and-play: uma única chamada `AddGuaraDashboard()` que registra a API, serve os assets estáticos da SPA e monta a rota (`/guara`). Este pacote é a **cola**, sem lógica de dados própria.

## Scope

### In

- `AddGuaraDashboard()` / `MapGuaraDashboard(path)` — registra a API (Spec 022) e serve os assets embutidos da SPA (Spec 024).
- Servir a SPA como **arquivos estáticos embutidos** no assembly (sem dependência de disco).
- Configuração de rota base (default `/guara`), auth (Spec 020/021) e CORS.

### Out

- Endpoints de dados (Spec 022) e UI (Spec 024).
- Qualquer acesso a storage concreto.

## Domain Model

- Composição: mapeia a API sob `{base}/api` e a SPA sob `{base}`.
- Assets da SPA embutidos como recursos do assembly (`EmbeddedFileProvider`).
- `DashboardOptions` — `BasePath`, `RequireAuthorization` (default true).

## API Contract

```csharp
namespace Microsoft.Extensions.DependencyInjection;
public static class GuaraDashboardExtensions
{
    public static IGuaraBuilder AddGuaraDashboard(this IGuaraBuilder builder, Action<DashboardOptions>? configure = null);
}

namespace Microsoft.AspNetCore.Builder;
public static class GuaraDashboardEndpointExtensions
{
    public static IEndpointRouteBuilder MapGuaraDashboard(this IEndpointRouteBuilder endpoints, string basePath = "/guara");
}

public sealed class DashboardOptions
{
    public string BasePath { get; set; } = "/guara";
    public bool RequireAuthorization { get; set; } = true; // seguro por padrão
}
```

## Authorization

`RequireAuthorization=true` por padrão (Spec 020/021). Servir a SPA não expõe dados — os dados só vêm da API autorizada. A composição expõe o encadeamento **`AddGuaraDashboard(dash => dash.UseGuaraAuthentication(...))`** com as regras de acesso fluentes e a **página de login embutida** (logo do Guará) — [Spec 037](037-dashboard-autenticacao.md).

## Edge Cases & Failure Modes

- **BasePath conflitante** com rotas do app → detectar/avisar; configurável.
- **SPA não embutida** (build sem assets) → erro claro no startup.
- **Auth desligada** → aviso forte (dashboard exposto).
- **Assets versionados** → cache-busting por hash no nome do arquivo.
- **Stream SSE do dashboard** → o proxy reverso na frente do host deve desabilitar buffering na rota de stream (`/guara/api/v1/stream`); ver `Infra/nginx` (Spec 022).

## Non-Functional Requirements

- **Zero dependência de disco** (assets embutidos) — deploy simples, AOT/single-file-friendly.
- Superfície mínima (um `Add` + um `Map`).
- Não introduz acoplamento entre API e UI além do contrato HTTP.

## Integrations

Compõe Spec 022 (API) e Spec 024 (SPA); integra ao pipeline ASP.NET Core do host.

## Acceptance Criteria

- **AC-1 — Plug-and-play.** *Dado* `AddGuaraDashboard()` + `MapGuaraDashboard()`, *então* o dashboard fica acessível em `/guara`.
- **AC-2 — API sob base.** *Dado* a montagem, *então* a API responde em `/guara/api/v1/*`.
- **AC-3 — Assets embutidos.** *Dado* deploy sem arquivos extras em disco, *então* a SPA carrega (recursos embutidos).
- **AC-4 — Seguro por padrão.** *Dado* config default, *então* o dashboard exige autorização.
- **AC-5 — Base configurável.** *Dado* `BasePath="/jobs"`, *então* tudo é servido sob `/jobs`.
- **AC-6 — Falha clara sem assets.** *Dado* build sem SPA embutida, *então* erro explícito no startup.

## Deferred Decisions

- **DD-1 — Empacotamento dos assets.** *Fallback:* recursos embutidos no assembly gerados no build da SPA (Spec 024). *Revisão:* Spec 024/CI.
- **DD-2 — Base path default.** *Fallback:* `/guara`. *Revisão:* feedback.

## Open Questions

_(vazio)_
