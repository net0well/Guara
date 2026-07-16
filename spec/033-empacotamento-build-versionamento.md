# Spec 033: Empacotamento, Build & Versionamento

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Escopo:** solution-wide (infra de build/pacote)
**Depende de:** todas as specs de pacote (001–032)
**Docs de referência:** [ARCHITECTURE](../docs/ARCHITECTURE.md) · [ADR-0006](../docs/adr/0006-uma-extensao-addguara-por-pacote.md) · [ADR-0008](../docs/adr/0008-native-aot-e-trimming.md)

## Problem

Para o Guará ser **instalável e mantível** por muita gente no NuGet (como Hangfire/EF Core), não basta compilar: cada pacote precisa de metadados corretos, build determinístico, símbolos, docs XML, versão semântica automática e **garantia de compatibilidade de API** entre versões. Esta spec define a infra de build da solution inteira.

## Scope

### In

- **Multi-target**: `net8.0` (LTS) + `net10.0` em todos os pacotes runtime; AOT/trimming plenos no `net10.0`.
- **`.slnx`** + **`Directory.Build.props`** (settings comuns) + **Central Package Management** (`Directory.Packages.props`).
- **Metadados de pacote** por projeto: `PackageId` (`Guara.*` / `Guara.Pro.*`), `Description`, `Authors`, `PackageTags`, `PackageReadmeFile`, `PackageIcon`, `PackageProjectUrl`, `RepositoryUrl`, `PackageLicenseExpression`/`PackageLicenseFile`.
- **Build determinístico** + **SourceLink** + `EmbedUntrackedSources`; **símbolos** `.snupkg`.
- **XML docs** na API pública (`GenerateDocumentationFile`; warnings de doc faltando tratados).
- **Versionamento semântico automático** (MinVer ou Nerdbank.GitVersioning) a partir de tags git.
- **Compatibilidade de API pública**: `Microsoft.CodeAnalysis.PublicApiAnalyzers` (`PublicAPI.Shipped/Unshipped.txt`) por pacote público — enforça o **extend-only** ([Spec 001](001-guara-abstractions.md)).
- `global.json` fixando o SDK.

### Out

- O pipeline em si (é da [Spec 034](034-cicd-release.md)).
- Licenciamento/governança (é da [Spec 035](035-governanca-licenciamento-docs.md)).

## Domain Model

- `Directory.Build.props` na raiz define: `LangVersion`, `Nullable=enable`, `ImplicitUsings`, `TreatWarningsAsErrors`, `TargetFrameworks`, determinismo, SourceLink, símbolos, política de docs.
- `Directory.Packages.props` centraliza versões (CPM); versões compartilhadas por variável para pacotes relacionados.
- `Guara.Pro.*` marcados como pacotes comerciais (metadados de licença próprios — Spec 035).

## API Contract

Não há API .NET; o "contrato" é o **conjunto de propriedades MSBuild/metadados** obrigatórias por pacote e os arquivos `PublicAPI.*.txt`.

## Authorization

N/A.

## Edge Cases & Failure Modes

- **Diferença por TFM** → APIs/otimizações específicas de `net10` sob `#if NET10_0_OR_GREATER`; nada quebra no `net8`.
- **Mudança de API pública não declarada** → build falha (PublicApiAnalyzers) até atualizar `PublicAPI.Unshipped.txt`.
- **Símbolos/SourceLink ausentes** → build de release falha (gate de qualidade).
- **Doc XML faltando em símbolo público** → warning-as-error.
- **Trimming/AOT warnings** no `net10` → falham o build (gate).

## Non-Functional Requirements

- Build reproduzível/determinístico; restauração via CPM (sem versões soltas nos `.csproj`).
- Todo pacote público tem README próprio (`PackageReadmeFile`) e docs XML.
- Pacotes runtime passam publish AOT (`net10`) no CI ([Spec 034](034-cicd-release.md)).

## Acceptance Criteria

- **AC-1 — Multi-target.** *Dado* qualquer pacote runtime, *então* ele compila para `net8.0` e `net10.0`.
- **AC-2 — Metadados.** *Dado* `dotnet pack`, *então* cada `.nupkg` tem Id/Description/Tags/README/ícone/licença/RepositoryUrl.
- **AC-3 — Símbolos + SourceLink.** *Dado* um pacote de release, *então* há `.snupkg` e o SourceLink resolve para o commit.
- **AC-4 — Docs XML.** *Dado* um símbolo público sem doc, *então* o build falha.
- **AC-5 — API-compat.** *Dada* a remoção/alteração de um membro público sem atualizar `PublicAPI.*.txt`, *então* o build falha.
- **AC-6 — Versão automática.** *Dada* uma tag `v1.2.3`, *então* os pacotes saem `1.2.3` sem edição manual.
- **AC-7 — CPM.** *Dado* qualquer `.csproj`, *então* não há `Version=` inline (todas centralizadas).

## Deferred Decisions

- **DD-1 — Ferramenta de versão.** *Fallback:* MinVer (simples, baseada em tags). *Revisão:* início do setup.
- **DD-2 — netstandard2.0.** *Fallback:* **não** incluir no 1.0 (decisão de TFM = net8+net10); reavaliar se surgir demanda por .NET Framework. *Revisão:* pós-1.0.
- **DD-3 — Assinatura de assembly (strong name).** *Fallback:* strong-name nos pacotes públicos para compat de bind. *Revisão:* setup.

## Open Questions

_(vazio)_
