# Spec 003: `Guara.Serialization` — Serialização

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Componente:** `Guara.Serialization`
**Depende de:** [Spec 001](001-guara-abstractions.md)
**Docs de referência:** [performance](../docs/performance.md) · [ADR-0005](../docs/adr/0005-source-generators-para-registro.md) · [ADR-0008](../docs/adr/0008-native-aot-e-trimming.md)

## Problem

Jobs precisam ser **persistidos e transportados**: os argumentos do método, metadados e resultados viram bytes no storage e voltam a objetos na execução. Isso precisa ser **rápido, sem reflection** (AOT), **seguro** (sem instanciar tipos arbitrários vindos do payload) e **tolerante a versão** (um job serializado ontem deve rodar após um deploy). Essa responsabilidade — e **nada além** — é de `Guara.Serialization`.

Como o projeto será open-source com muitos usuários e deploys em rolling upgrade, a **compatibilidade de payload entre versões** e a **segurança de desserialização** são requisitos de primeira classe.

## Scope

### In

- Corpo do contrato `ISerializer` (declarado em [Spec 001](001-guara-abstractions.md)).
- Implementação **default** sobre `System.Text.Json` com **source generators** (zero reflection).
- (De)serialização de `JobDescriptor.Args`, metadados/headers e resultados.
- **Registro de tipos serializáveis** por source generator + **allowlist** (segurança).
- Política de **tolerância a versão** (campos desconhecidos ignorados; defaults para ausentes).

### Out

- Escolha de *onde* persistir (é do `Guara.Storage`).
- Extensão `AddGuara...()` (wiring é do `Hosting`).
- Formatos alternativos concretos (MessagePack/Protobuf) — plugáveis depois via `ISerializer` (ver DD-1), fora do MVP.

## Domain Model

- **`ISerializer`** — contrato único de (de)serialização, agnóstico de formato.
- **`SerializedPayload`** — bytes + versão de esquema + discriminador de tipo (opaco).
- **Type registry** — mapa `discriminador ⇄ Type`, **gerado em compilação**; desserialização só resolve tipos do registry (nunca de nome qualificado no payload).

## API Contract

```csharp
namespace Guara.Abstractions; // contrato vive em Abstractions (catálogo da Spec 001)

public interface ISerializer
{
    ReadOnlyMemory<byte> Serialize<T>(T value);
    T? Deserialize<T>(ReadOnlySpan<byte> data);

    // formato "aberto" para args cujo tipo estático não é conhecido no call site
    ReadOnlyMemory<byte> SerializeArgs(ReadOnlySpan<object?> args);
    object?[] DeserializeArgs(ReadOnlySpan<byte> data);
}

// Implementação default (Guara.Serialization)
public sealed class SystemTextJsonSerializer(SerializerTypeRegistry registry, JsonSerializerOptions? options = null) : ISerializer;
```

> **Implementação (2026-07-16):** `SerializeArgs` usa o **tipo de runtime** de cada argumento
> (correto para polimorfismo) em vez do parâmetro `argTypes` originalmente esboçado — assinatura
> simplificada para `ReadOnlySpan<object?>`. Envelope versionado
> `{"v":1,"args":[{"t":"<discriminador>","d":<json>}]}`; `t:null` representa argumento nulo.
> A allowlist é o `SerializerTypeRegistry` (mapa bidirecional discriminador ⇄ tipo;
> `CreateDefault()` pré-registra primitivos comuns) — registro manual até o
> `Guara.SourceGenerators` (spec 029) preenchê-lo em compilação.

- Retorna `ReadOnlyMemory<byte>`/`Span<byte>` (baixa alocação, [performance](../docs/performance.md)).
- Referencia **apenas** `Guara.Abstractions` + `System.Text.Json`.

## Authorization

N/A. Porém, a **desserialização segura** (allowlist) é uma fronteira de segurança: impede execução de tipos não registrados vindos de um storage comprometido.

## Edge Cases & Failure Modes

- **Tipo não registrado** no payload → falha explícita (`SerializationException`), nunca instancia tipo arbitrário.
- **Campo desconhecido** (payload mais novo que o código) → ignorado.
- **Campo ausente** (payload mais antigo) → valor default; sem exceção.
- **Payload corrompido** → erro determinístico, job marcado `Failed` com motivo (não derruba o worker).
- **Ciclos/`null`/polimorfismo** → discriminador de tipo explícito; sem `TypeNameHandling` inseguro.
- **Cultura/tempo** → datas em UTC ISO-8601; números invariantes de cultura.

## Non-Functional Requirements

- **Zero reflection** em runtime (`JsonSerializerContext` gerado) — AOT/Trimming-safe ([ADR-0005](../docs/adr/0005-source-generators-para-registro.md), [ADR-0008](../docs/adr/0008-native-aot-e-trimming.md)).
- Baixa alocação; caminho quente sem cópias desnecessárias.
- Thread-safe (serializer sem estado mutável).
- **Compatibilidade de esquema** versionada e coberta por testes de snapshot (`Verify`).

## Integrations

Nenhuma externa. Consumido por `Storage` (persistência) e `Executor` (materialização de args).

## Acceptance Criteria

- **AC-1 — Round-trip.** *Dado* um `JobDescriptor` com args, *quando* serializado e desserializado, *então* os args são equivalentes ao original.
- **AC-2 — Zero reflection/AOT.** *Dado* `PublishAot=true`, *então* a (de)serialização funciona sem warnings de trim/AOT.
- **AC-3 — Allowlist.** *Dado* um payload referenciando um tipo não registrado, *quando* desserializado, *então* falha explicitamente sem instanciar o tipo.
- **AC-4 — Tolerância a versão.** *Dado* um payload com campo extra (versão futura), *quando* desserializado pelo código atual, *então* o campo é ignorado e o job roda.
- **AC-5 — Ausência de campo.** *Dado* um payload antigo sem um campo novo, *então* o campo assume default sem erro.
- **AC-6 — Corrupção isolada.** *Dado* um payload corrompido, *então* o job vira `Failed` com motivo e o worker segue vivo.
- **AC-7 — Determinismo cultural.** *Dado* execução em máquinas com culturas diferentes, *então* o payload é idêntico (UTC, invariante).

## Deferred Decisions

- **DD-1 — Formatos alternativos.** *Fallback:* só `System.Text.Json` source-gen no MVP; `MessagePack`/`Protobuf` plugáveis via `ISerializer` depois (alinhado à skill `dotnet-skills:serialization` — preferir formatos com esquema). *Revisão:* pós-MVP.
- **DD-2 — Estratégia de discriminador de tipo.** *Fallback:* discriminador curto e estável gerado pelo source generator (não nome de assembly). *Revisão:* Spec 029 (`Guara.SourceGenerators`).
- **DD-3 — Versão de esquema por job.** *Fallback:* campo `schemaVersion` no `SerializedPayload`; migração de payload é responsabilidade opcional do autor do job. *Revisão:* pós-MVP.

## Open Questions

_(vazio)_
