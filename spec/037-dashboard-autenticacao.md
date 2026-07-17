# Spec 037: Dashboard — Autenticação, Regras de Acesso e Página de Login

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Escopo:** feature — estende [Spec 020](020-guara-authentication.md), [Spec 021](021-guara-authorization.md), [Spec 023](023-guara-dashboard.md) e [Spec 024](024-guara-dashboard-angular.md)
**Licença:** OSS (core)
**Docs de referência:** [ADR-0010](../docs/adr/0010-api-do-usuario-em-portugues.md)

## Problem

Proteger o dashboard no Hangfire exige implementar `IDashboardAuthorizationFilter` na mão — e a experiência (401 seco, sem página de login própria) é espartana. O Guará precisa de: (1) uma **API fluente de regras de acesso** cobrindo os casos comuns sem código custom; (2) um **ponto de extensão** equivalente ao `IDashboardAuthorizationFilter` com acesso ao `HttpContext`; e (3) uma **página de login própria, elegante e com a identidade do Guará** — visivelmente melhor que a do Hangfire.

## Scope

### In

- **`UseGuaraAuthentication(...)`** encadeado ao dashboard (extensão DI em inglês — ADR-0010; **regras em português**):

```csharp
builder.Services
    .AddGuara()
    .AddGuaraDashboard(dash => dash
        .UseGuaraAuthentication(auth => auth
            .PermitirApenasLogados()                 // exige usuário autenticado
            .ExigirPapel("Admin")                    // apenas administradores (role)
            .ExigirClaim("departamento", "ti")       // verifica uma claim
            .PermitirApenasIpsInternos()));          // faixas privadas + loopback
```

- **Regras embutidas**: `PermitirApenasLogados()`, `ExigirPapel(nome)`, `ExigirClaim(tipo[, valor])`, `PermitirApenasIpsInternos()`, `PermitirIps("10.0.0.0/8", ...)` (CIDR), `ComLoginFixo(usuario, senha)`, `ComRegra<T>()`/`ComRegra(instancia)`.
- **Combinação de regras**: default **E** (todas precisam passar); grupo **OU** via `QualquerUma(g => g.ExigirPapel("Admin").ExigirClaim("guara", "admin"))`.
- **Ponto de extensão** (`IDashboardAccessRule`) com **`DashboardContext`** — equivalente ao `GetHttpContext()` do Hangfire.
- **Login fixo** (`ComLoginFixo`): credenciais simples para cenários pequenos/interno — com proteções (hash, rate limiting, cookie assinado).
- **Página de login própria**: logo do Guará, tema claro/escuro, i18n pt-BR/en, acessível (WCAG), responsiva — parte da SPA (Spec 024), servida embutida (Spec 023).

### Out

- Provedores de identidade (JWT/OIDC/cookie do host) — continuam sendo da [Spec 020](020-guara-authentication.md); esta spec **consome** a identidade estabelecida.
- Permissões por ação (`guara:view`, `guara:delete`...) — [Spec 021](021-guara-authorization.md); as regras daqui decidem **entrada** no dashboard, as permissões decidem **o que se pode fazer** dentro dele.

## Domain Model

- **`IDashboardAccessRule`** — o contrato de regra (o "IDashboardAuthorizationFilter" do Guará). Todas as regras embutidas o implementam.
- **`DashboardContext`** — contexto da requisição ao dashboard: `HttpContext` (acesso completo, como o `GetHttpContext()` do Hangfire), `User` (`ClaimsPrincipal`), `RemoteIp`, `Path`.
- **Avaliação**: pipeline de regras roda **antes** de qualquer endpoint do dashboard (API e SPA); todas passam (E) → segue; alguma falha → desafio de autenticação (página de login/401) ou 403.
- **`ComLoginFixo`**: valida credenciais → emite **cookie próprio assinado** (expiração configurável); senha comparada por **hash** (nunca armazenada/logada em claro).

## API Contract

```csharp
namespace Guara.Dashboard;

public interface IDashboardAccessRule
{
    /// Decide se a requisição pode acessar o dashboard.
    ValueTask<bool> AutorizarAsync(DashboardContext contexto, CancellationToken ct);
}

public sealed class DashboardContext
{
    public HttpContext HttpContext { get; }          // = GetHttpContext() do Hangfire
    public ClaimsPrincipal User { get; }
    public IPAddress? RemoteIp { get; }
    public PathString Path { get; }
}
```

Regra customizada (equivalente direto do Hangfire):

```csharp
public sealed class SomenteHorarioComercial : IDashboardAccessRule
{
    public ValueTask<bool> AutorizarAsync(DashboardContext contexto, CancellationToken ct)
    {
        var hora = TimeProvider.System.GetLocalNow().Hour;
        var autenticado = contexto.User.Identity?.IsAuthenticated == true;
        return ValueTask.FromResult(autenticado && hora is >= 8 and < 18);
    }
}

// registro:
.UseGuaraAuthentication(auth => auth.ComRegra<SomenteHorarioComercial>());
```

Login fixo (cenários pequenos/rede interna):

```csharp
.UseGuaraAuthentication(auth => auth
    .ComLoginFixo(
        usuario: "guara_admin",
        senha: builder.Configuration["Guara:Dashboard:Senha"]!)   // env/secret — nunca literal
    .PermitirApenasIpsInternos());
```

## Authorization

Camadas em ordem: identidade (Spec 020) → **regras de acesso (esta spec)** → permissões por ação (Spec 021). **Deny by default** permanece: sem `UseGuaraAuthentication` e sem esquema do host, o dashboard nega anônimos (Spec 020 AC-1).

## Edge Cases & Failure Modes

- **Nenhuma regra configurada** → comportamento da Spec 020 (exige autenticado; anônimo só com `AllowAnonymousDashboard=true` explícito).
- **Regra falha para usuário não autenticado** → redireciona à **página de login** (login fixo/cookie) ou ao challenge do esquema do host (OIDC/JWT); autenticado sem acesso → **403** com página amigável.
- **Login fixo — força bruta** → rate limiting por IP + lockout progressivo; tentativas logadas (sem a senha).
- **Senha fixa em texto claro no appsettings** → **aviso forte** no startup recomendando env/user-secrets; em produção sem HTTPS → aviso.
- **`PermitirApenasIpsInternos` atrás de proxy** → respeita `X-Forwarded-For` **somente** se `ForwardedHeaders` do host estiver configurado (documentado — senão usa o IP direto; nunca confia no header cru).
- **Regra custom que lança** → tratada como **negado** (fail-safe) e logada.
- **SSE (`/api/v1/stream`)** → passa pelas mesmas regras (a conexão longa é autorizada na abertura).

## Non-Functional Requirements

- Avaliação de regras de baixo overhead (uma passada por requisição; resultados por requisição, sem estado global).
- Página de login **embutida** (assets da SPA — zero dependência externa), com identidade visual do Guará (logo lobo-guará), tema claro/escuro, pt-BR/en, WCAG.
- Segurança: cookie assinado/HttpOnly/SameSite, hash de senha (PBKDF2/Argon2 via primitivas .NET), rate limiting, sem segredos em log (ADR-0009: sem terceiros).

## Integrations

Consome identidade da Spec 020; antecede as permissões da Spec 021; página de login vive na SPA (Spec 024) e é servida pela composição (Spec 023); `PermitirApenasIpsInternos` documenta interação com o proxy reverso (`Infra/nginx`).

## Acceptance Criteria

- **AC-1 — Apenas logados.** *Dado* `PermitirApenasLogados()`, *então* anônimo é levado ao login e autenticado entra.
- **AC-2 — Apenas administradores.** *Dado* `ExigirPapel("Admin")`, *então* usuário sem a role recebe 403; com a role, entra.
- **AC-3 — Claim.** *Dado* `ExigirClaim("departamento", "ti")`, *então* só quem tem a claim entra.
- **AC-4 — IPs internos.** *Dado* `PermitirApenasIpsInternos()`, *então* requisições de IP público são negadas mesmo autenticadas.
- **AC-5 — Combinação E.** *Dado* `ExigirPapel("Admin").PermitirApenasIpsInternos()`, *então* é preciso passar em **ambas**.
- **AC-6 — Combinação OU.** *Dado* `QualquerUma(g => g.ExigirPapel("Admin").ExigirClaim("guara","admin"))`, *então* passar em **uma** basta.
- **AC-7 — Regra custom + HttpContext.** *Dado* um `IDashboardAccessRule` registrado via `ComRegra<T>()`, *então* ele recebe `DashboardContext` com `HttpContext` completo e sua decisão é respeitada.
- **AC-8 — Login fixo.** *Dado* `ComLoginFixo(u, s)`, *então* a página de login autentica, emite cookie assinado e senhas erradas sofrem rate limiting.
- **AC-9 — Página de login.** *Dado* o fluxo de login, *então* a página exibe a logo do Guará, funciona em claro/escuro e pt-BR/en, e passa auditoria básica de acessibilidade.
- **AC-10 — Fail-safe.** *Dado* uma regra que lança exceção, *então* o acesso é negado e o erro logado (nunca "aberto por falha").

## Deferred Decisions

- **DD-1 — Nome da extensão.** *Decisão:* `UseGuaraAuthentication` (extensão DI em inglês, ADR-0010); regras em português. *Alternativa `UseGuaraAutenticacao` descartada por consistência com `AddGuara`/`Use*Storage`.*
- **DD-2 — Hash do login fixo.** *Fallback:* PBKDF2 (primitivas .NET, sem terceiros); aceitar senha em claro só com aviso, ou hash pré-computado via CLI (`guara hash-senha`). *Revisão:* implementação (spec 027 pode ganhar o comando).
- **DD-3 — Lockout.** *Fallback:* rate limit por IP (janela deslizante) + lockout progressivo; limites configuráveis. *Revisão:* implementação.

## Open Questions

_(vazio)_
