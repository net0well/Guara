# Spec 017: `Guara.OpenTelemetry` — Exporters OpenTelemetry

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.OpenTelemetry`
**Depende de:** [Spec 016 (`Guara.Diagnostics`)](016-guara-diagnostics.md), [Spec 001](001-guara-abstractions.md)
**Docs de referência:** [performance](../docs/performance.md) · [ADR-0008](../docs/adr/0008-native-aot-e-trimming.md)

## Problem

`Guara.Diagnostics` (Spec 016) produz métricas e traces via primitivas do .NET. Para levá-los a um backend (Jaeger, Prometheus, Grafana, vendor OTLP), é preciso **registrar as fontes** (`ActivitySource`/`Meter` do Guará) no pipeline do OpenTelemetry. Este pacote faz **apenas** essa ponte — mantendo a decisão de exporter com o usuário.

## Scope

### In

- Extensões que registram `ActivitySource("Guara")` e `Meter("Guara")` no OpenTelemetry (`AddGuaraInstrumentation()` para `TracerProviderBuilder`/`MeterProviderBuilder`).
- Enriquecimento de spans com convenções semânticas relevantes.

### Out

- Escolha/configuração de exporter concreto (OTLP/Prometheus) — é do usuário (skill `dotnet-claude-kit:opentelemetry`).
- Produção das métricas/traces em si (é da Spec 016).

## Domain Model

- Ponte fina: adiciona as fontes nomeadas do Guará às pipelines OTel do usuário.
- Sem estado; sem lógica de negócio.

## API Contract

```csharp
namespace OpenTelemetry.Trace  { public static class GuaraTracing  { public static TracerProviderBuilder AddGuaraInstrumentation(this TracerProviderBuilder b); } }
namespace OpenTelemetry.Metrics{ public static class GuaraMetrics  { public static MeterProviderBuilder  AddGuaraInstrumentation(this MeterProviderBuilder  b); } }
```

## Authorization

N/A. Não exporta payloads; apenas metadados de telemetria já sanitizados pela Spec 016.

## Edge Cases & Failure Modes

- **OTel ausente/mal configurado** → o registro é no-op seguro; nunca quebra o app.
- **Nome de fonte divergente** → usa as constantes de `GuaraDiagnostics` (Spec 016) para evitar erro de digitação.

## Non-Functional Requirements

- Overhead zero quando não há listeners.
- AOT/Trimming-safe.

## Integrations

OpenTelemetry .NET SDK (pacotes `OpenTelemetry.*`); consumido pelo host que já usa OTel.

## Acceptance Criteria

- **AC-1 — Traces fluem.** *Dado* um `TracerProvider` com `AddGuaraInstrumentation()`, *então* spans de jobs chegam ao exporter configurado.
- **AC-2 — Métricas fluem.** *Dado* um `MeterProvider` com `AddGuaraInstrumentation()`, *então* as métricas `guara.*` são exportadas.
- **AC-3 — Fonte correta.** *Dado* o registro, *então* usa exatamente `GuaraDiagnostics.ActivitySourceName`/`MeterName`.
- **AC-4 — AOT.** *Dado* `PublishAot=true`, *então* sem warnings originados aqui.
- **AC-5 — Sem exporter opinado.** *Dado* o pacote, *então* ele não força um exporter específico.

## Deferred Decisions

- **DD-1 — Convenções semânticas.** *Fallback:* seguir as convenções OTel de "messaging/jobs" quando estáveis. *Revisão:* conforme a spec OTel evoluir.

## Open Questions

_(vazio)_
