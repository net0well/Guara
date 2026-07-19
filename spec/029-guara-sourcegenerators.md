# Spec 029: `Guara.SourceGenerators` — Source Generators

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.SourceGenerators`
**Depende de:** — (geradores de compilação; sem dependência de runtime)
**Docs de referência:** [performance](../docs/performance.md) · [ADR-0005](../docs/adr/0005-source-generators-para-registro.md) · [ADR-0008](../docs/adr/0008-native-aot-e-trimming.md)

## Problem

A regra de **zero reflection em runtime** ([ADR-0005](../docs/adr/0005-source-generators-para-registro.md)) só é possível se a **descoberta e o registro** acontecerem em tempo de compilação: quais tipos são jobs, como invocá-los sem reflection, como registrar middlewares/handlers, e como traduzir `Expression` → `JobDescriptor`. `Guara.SourceGenerators` emite esse código, garantindo AOT/Trimming e erros em build (não em produção).

## Scope

### In

- **Registry de jobs**: descobre tipos/métodos de job (por atributo/marcador) e gera o `IJobInvoker` (dispatch sem reflection, Spec 008).
- **Registro de middlewares/handlers** em ordem, sem varredura de assembly.
- **Tradução de `Expression`** (`() => svc.Metodo(arg)`) → `JobDescriptor` (Spec 019), com discriminador de tipo estável (Spec 003 DD-2).
- **`JsonSerializerContext`** para os tipos serializáveis (coopera com Spec 003).
- Marcador de assembly (`[assembly: GuaraJobs]`) que dispara a geração (Spec 009 DD-3).

### Out

- Comportamento em runtime (o código gerado roda; o gerador não).
- Regras arquiteturais → `Guara.Analyzers` (Spec 028).

## Domain Model

- Pipeline **incremental** (skill `dotnet-skills:roslyn-incremental-generator-specialist`): parser (coleta símbolos) separado do emitter (gera código).
- Saídas: `GuaraJobRegistry.g.cs`, `GuaraJobInvoker.g.cs`, `GuaraSerializerContext.g.cs`, registro de middlewares.

## API Contract

Não há API .NET pública convencional; o "contrato" é o **código gerado** e seus pontos de extensão (atributos/marcadores):

```csharp
[GuaraJob]                 // marca um método como job
[assembly: GuaraJobs]      // habilita a geração no assembly
```

O generator também lê os **atributos de comportamento** ([Spec 036](036-atributos-de-job.md) — `[GuaraFila]`, `[GuaraRetentativas]`, `[GuaraDesabilitarConcorrencia]`, `[GuaraTempoLimite]`, `[GuaraPularSeAnteriorEmExecucao]`) **em compilação** e os materializa como metadados no registry gerado — zero reflection em runtime; placeholders de chave (`{0}`) validados com erro de compilação.

Tipos gerados implementam contratos da Spec 001/008 (`IJobInvoker`) e cooperam com Spec 003 (serializer context).

## Authorization

N/A.

## Edge Cases & Failure Modes

- **Job não descoberto** (sem marcador) → diagnóstico em build orientando a marcar; enfileirar tipo desconhecido falha explicitamente.
- **Expression não suportada** (não é chamada simples) → **erro de compilação** com mensagem clara (não falha em runtime).
- **Assinatura de job inválida** (parâmetro não serializável) → erro de build.
- **Tipo genérico/aberto** → tratado ou rejeitado explicitamente.
- **Determinismo** → geração determinística (mesma entrada → mesma saída), essencial para builds reproduzíveis.

## Non-Functional Requirements

- **Incremental e rápido** (não degrada IDE/build); parser/emitter separados.
- Saída **AOT/Trimming-safe** e sem reflection ([ADR-0008](../docs/adr/0008-native-aot-e-trimming.md)).
- Geração determinística; coberta por testes de snapshot (`Verify`) do código gerado.

## Integrations

Gera o dispatch usado pelo `Guara.Executor` (Spec 008), o registry consumido por `Guara.Hosting` (Spec 009), o contexto de serialização da Spec 003 e a tradução de expressões da Spec 019.

## Acceptance Criteria

- **AC-1 — Dispatch sem reflection.** *Dado* um método `[GuaraJob]`, *então* o gerador emite um `IJobInvoker` que o invoca sem reflection.
- **AC-2 — AOT.** *Dado* `PublishAot=true`, *então* jobs enfileiram e executam sem warnings de trim/AOT.
- **AC-3 — Expression compilada.** *Dado* `client.EnfileirarAsync(() => svc.Fazer(x))`, *então* o gerador produz o `JobDescriptor` correspondente em compilação.
- **AC-4 — Expression inválida.** *Dado* uma expressão não suportada, *então* erro de compilação (não runtime).
- **AC-5 — Serializer context.** *Dado* tipos de argumentos, *então* o `JsonSerializerContext` gerado cobre todos (coopera com Spec 003).
- **AC-6 — Determinismo.** *Dada* a mesma entrada, *então* a saída gerada é idêntica (build reproduzível).
- **AC-7 — Incremental.** *Dado* uma edição pequena, *então* apenas o necessário é regenerado (sem travar a IDE).

## Deferred Decisions

- **DD-1 — Marcador de descoberta.** *Fallback:* atributo `[GuaraJob]` + `[assembly: GuaraJobs]`. *Revisão:* implementação.
- **DD-2 — Discriminador de tipo.** *Fallback:* nome curto estável gerado (não assembly-qualified) — herda Spec 003 DD-2. *Revisão:* resolvida aqui.
- **DD-3 — Suporte a genéricos.** *Fallback:* jobs não-genéricos no MVP; genéricos avaliados depois. *Revisão:* pós-MVP.

> **Implementação (2026-07-19):** generator incremental entregue (`GuaraJobGenerator`, netstandard2.0; parser/emitter separados, pipeline só com modelos equatable — `ForAttributeWithMetadataName` + `EquatableArray`). Por assembly com `[GuaraJob]`, emite: **`AddGuaraJobs()`** (registra as classes de job como transient e um `IJobModule` gerado), o **módulo de invocação sem reflection** (resolve a classe no DI, desserializa os argumentos e chama o método — AC-1) com os **metadados dos atributos da spec 036** materializados (`JobExecutionMetadata`: fila, retentativas, tempo limite, pular-se-anterior, chave de concorrência com placeholders formatados por delegate gerado — placeholders inválidos são **erro de compilação GUARA0103**), e as **factories tipadas de descritor** (`{Classe}Guara.{Metodo}(args)` — a resolução da spec 019). Diagnósticos: GUARA0102 (tipo de argumento fora do conjunto), 0105 (genérico), 0106 (retorno ≠ Task/ValueTask/void), 0107 (CancellationToken fora do fim), 0108 (discriminador duplicado), 0109 (ref/out/in). **DD-1 ajustada:** a geração dispara pela presença de `[GuaraJob]` — o marcador de assembly ficou reservado (fricção desnecessária). **DD-2 resolvida:** discriminador = `Namespace.Tipo` (sem assembly; renomear namespace invalida jobs pendentes — documentado, igual Hangfire). **AC-5 ajustado (limitação real de generators):** um generator **não vê a saída de outro** — o `JsonSerializerContext` gerado nunca seria processado pelo generator do System.Text.Json; a serialização de argumentos é **Utf8JsonWriter/Reader emitidos diretamente** (array posicional) para o conjunto suportado: números, string, bool, char, Guid, DateTime/DateTimeOffset, TimeSpan, DateOnly/TimeOnly, Uri, enums e anuláveis — fora disso, erro de build (nunca falha em produção). Snapshot: determinismo coberto por teste de igualdade byte-a-byte (AC-6) + **compile-check** do código gerado + **e2e real** (Hosting.Tests consome o generator como analyzer e executa jobs gerados); Verify entra com a suíte de benchmarks (spec 033). AC-7 (incremental) garantido pela disciplina de modelos equatable.

## Open Questions

_(vazio)_
