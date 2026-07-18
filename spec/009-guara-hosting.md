# Spec 009: `Guara.Hosting` — Hosting, DI e Bootstrap

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Hosting`
**Depende de:** [Spec 001](001-guara-abstractions.md), [Spec 002](002-guara-core.md)
**Docs de referência:** [patterns](../docs/patterns.md) · [naming-conventions](../docs/naming-conventions.md) · [ADR-0006](../docs/adr/0006-uma-extensao-addguara-por-pacote.md)

## Problem

Todo o resto precisa ser **composto e iniciado** dentro de uma aplicação .NET. Espelhando o ASP.NET Core Hosting, `Guara.Hosting` fornece o ponto de entrada `AddGuara()`, o `IGuaraBuilder`, o binding+validação de opções e o registro do `IHostedService` que dá vida ao servidor — **sem** conhecer providers concretos. É a primeira coisa que um usuário do framework toca; precisa ser mínimo e óbvio.

## Scope

### In

- **`AddGuara()`** (o único ponto de entrada do núcleo) → devolve `IGuaraBuilder`.
- Implementação de `IGuaraBuilder` (expõe `IServiceCollection Services`).
- Registro dos serviços de núcleo (pipeline builder, event bus, pool de `JobContext`, máquina de estados, middlewares genéricos, `IGuaraClient`).
- **Binding e validação de opções** no startup (`IValidateOptions`, falha cedo).
- Registro do `IHostedService` que delega ao `Guara.Server` (Spec 010).
- Wiring do **registry de jobs gerado** (`IJobInvoker`, Spec 029) a partir de um marcador de assembly.

### Out

- Lifecycle em runtime (start/stop dos motores) → `Guara.Server` (Spec 010).
- Providers concretos (`Use...Storage()` vêm de cada provider).
- Extensões dos motores (`AddGuaraScheduler` etc. vêm de cada pacote).

## Domain Model

- **`AddGuara(services, configure)`** — registra núcleo e devolve `IGuaraBuilder`.
- **`GuaraOptions`** — opções globais (nome do app, filas default, etc.), validadas no startup.
- **`AddGuaraServer()`** — conveniência que compõe scheduler+dispatcher+worker+executor+server (cada um pelo seu Add).
- Composição fluente: `AddGuara().UseXStorage().AddGuaraServer().AddGuaraDashboard()`.

## API Contract

```csharp
namespace Microsoft.Extensions.DependencyInjection; // namespace obrigatório (ADR-0006)

public static class GuaraServiceCollectionExtensions
{
    public static IGuaraBuilder AddGuara(this IServiceCollection services, Action<GuaraOptions>? configure = null);
}

public sealed class GuaraOptions
{
    public string ApplicationName { get; set; } = "guara";
    public string[] DefaultQueues { get; set; } = ["default"];
    // validado por IValidateOptions no startup
}
```

## Authorization

Não decide autorização; apenas **oferece o ponto de wiring** para `Guara.Authentication`/`Guara.Authorization` (Specs 020/021) se presentes.

## Edge Cases & Failure Modes

- **Opções inválidas** → falha no startup (`OptionsValidationException`), nunca em runtime silencioso.
- **Nenhum storage registrado** → erro claro no startup ("chame `Use...Storage()`").
- **Dois storages registrados** → erro claro (ambiguidade) ou o último vence, documentado (DD-2).
- **Jobs não descobertos** (marcador de assembly ausente) → aviso no startup; enfileirar tipo desconhecido falha explicitamente.
- **Registro manual duplicado** → `TryAdd*` evita sobrescrever; sem estado estático.

## Non-Functional Requirements

- Superfície pública **mínima** (um `AddGuara()` + `GuaraOptions`) — [ADR-0006](../docs/adr/0006-uma-extensao-addguara-por-pacote.md).
- Startup rápido; **validação antecipada** de configuração.
- AOT/Trimming-safe; wiring por código gerado, não varredura por reflection.
- Sem singleton estático; tudo via DI.

## Integrations

Integra-se ao Generic Host (.NET) via `IHostedService`; compõe Core (Spec 002) e delega runtime ao Server (Spec 010).

## Acceptance Criteria

- **AC-1 — Ponto único.** *Dado* o pacote, *então* expõe exatamente um método de entrada (`AddGuara`) no namespace `Microsoft.Extensions.DependencyInjection`.
- **AC-2 — Builder fluente.** *Dado* `AddGuara()`, *então* devolve `IGuaraBuilder` encadeável com `Use...`/`AddGuara...`.
- **AC-3 — Validação no startup.** *Dado* `GuaraOptions` inválido, *quando* o host inicia, *então* falha imediatamente com mensagem clara.
- **AC-4 — Sem storage.** *Dado* nenhum `Use...Storage()`, *então* o startup falha orientando a configurar um storage.
- **AC-5 — HostedService.** *Dado* `AddGuaraServer()`, *então* um `IHostedService` é registrado e inicia o `Guara.Server` no boot.
- **AC-6 — AOT.** *Dado* `PublishAot=true`, *então* o bootstrap funciona sem warnings de trim/AOT.
- **AC-7 — Sem provider concreto.** *Dado* o build, *então* `Guara.Hosting` não referencia nenhum `Guara.Storage.*` nem ASP.NET.

## Deferred Decisions

- **DD-1 — `AddGuara()` auto-inclui o servidor?** *Fallback:* **não**; `AddGuaraServer()` é explícito (permite processos só-cliente que apenas enfileiram). *Revisão:* pós-MVP conforme feedback.
- **DD-2 — Dois storages.** *Fallback:* erro no startup (ambiguidade explícita). *Revisão:* nenhuma.
- **DD-3 — Descoberta de jobs.** *Fallback:* source generator + marcador de assembly (`[assembly: GuaraJobs]`); sem varredura por reflection. *Revisão:* Spec 029.

> **Implementação (2026-07-18):** `AddGuara()` entregue no pacote `Guara.Hosting`: registra `GuaraOptions` (validação **eager** — falha na própria chamada, antes mesmo do boot), `TimeProvider`, `JobStateMachine` e `IEventPublisher` (`InProcessEventPublisher`), tudo com `TryAdd` (idempotente); devolve o `GuaraBuilder` interno. O **`IHostedService` vive no `Guara.Server`** e é registrado pelo `AddGuaraServer()` (AC-5 preservado) — assim o Hosting não referencia o Server; a validação "nenhum storage" acontece no `StartAsync` do hosted service com mensagem acionável (AC-4). Binding de `GuaraOptions` a partir de `IConfiguration` fica para a Spec 018.

## Open Questions

_(vazio)_
