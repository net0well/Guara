# Spec 035: Governança, Licenciamento & Docs

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Escopo:** repositório (open-source)
**Depende de:** [Spec 031](031-batches-pro.md), [Spec 033](033-empacotamento-build-versionamento.md), [Spec 034](034-cicd-release.md)

## Problem

Um projeto que "várias pessoas vão usar como o Hangfire" precisa de uma camada de **governança e licenciamento** clara: qual licença, o que é aberto vs comercial, como contribuir, como reportar vulnerabilidade e onde estão os docs. Sem isso, adoção e contribuição travam — e o modelo comercial (Pro) fica ambíguo.

## Scope

### In

- **Licenciamento dual**: core **Apache-2.0** (`LICENSE` + `NOTICE`), pacotes `Guara.Pro.*` sob **licença comercial** (`LICENSE-COMMERCIAL`/EULA) com validação por chave.
- **Arquivos de governança**: `README.md` (raiz), `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, templates de issue/PR, `CHANGELOG.md`.
- **Política de versionamento** (SemVer) e de compatibilidade (extend-only, [Spec 033](033-empacotamento-build-versionamento.md)).
- **Docs de usuário**: quickstart, conceitos, guia por provider, guia de dashboard, guia de migração do Hangfire, referência de API (gerada dos XML docs).
- **Matriz OSS × Pro** documentada e sem ambiguidade.

### Out

- Implementação do gerador de chaves Pro (detalhe de produto; contrato em [Spec 031](031-batches-pro.md)).

## Domain Model

- **OSS (Apache-2.0):** runtime, providers de storage, dashboard (tempo real + avançado), continuations, diagnostics/OTel, cluster/distributed, CLI, analyzers, source generators, extensions, configuration, auth.
- **Pro (comercial):** `Guara.Pro.Batches` (e futuros extras: analytics avançado, retenção estendida, suporte).
- Cada `.nupkg` declara sua licença nos metadados (Spec 033); README de cada pacote aponta o tier.

## API Contract

Não há API .NET; o "contrato" é o **conjunto de arquivos de governança** e a **matriz de licença** por pacote.

## Authorization

Direitos de merge/release restritos a maintainers (Spec 034). Vulnerabilidades via `SECURITY.md` (divulgação responsável, canal privado).

## Edge Cases & Failure Modes

- **Uso comercial e AOT** → Apache-2.0 permite uso em software proprietário sem obrigação de abertura, inclusive em publicação Native AOT/single-file (linkagem estática). Foi justamente o ponto que descartou a LGPL, que exigiria meio de relinkar nesse cenário.
- **Confusão OSS×Pro** → cada README/metadado declara o tier; o core **nunca** referencia pacote Pro.
- **Contribuição em código Pro** → CLA/Contributor License Agreement para contribuições que afetem pacotes comerciais.
- **Docs desatualizados** → referência de API gerada no CI (Spec 034) a partir dos XML docs.

## Non-Functional Requirements

- Licenças válidas e reconhecíveis pelo NuGet (`PackageLicenseExpression` = `Apache-2.0`; arquivo para o EULA comercial).
- Docs versionados e publicados (site estático) no release.
- Onboarding de contribuidor claro (build local em 1 comando; `CONTRIBUTING` com o fluxo de specs).

## Acceptance Criteria

- **AC-1 — Licenças presentes.** *Dado* o repositório, *então* há `LICENSE` (Apache-2.0), `NOTICE` e `LICENSE-COMMERCIAL` (Pro), e cada pacote declara a sua.
- **AC-2 — Governança.** *Dado* o repositório, *então* existem README, CONTRIBUTING, CODE_OF_CONDUCT, SECURITY, templates e CHANGELOG.
- **AC-3 — Matriz OSS×Pro.** *Dado* qualquer pacote, *então* fica inequívoco se é OSS ou Pro (README + metadados).
- **AC-4 — Core não depende de Pro.** *Dado* o grafo de dependências, *então* nenhum pacote OSS referencia `Guara.Pro.*` (enforçado por analyzer/CI).
- **AC-5 — Docs de usuário.** *Dado* um novo usuário, *então* o quickstart leva de "instalar" a "primeiro job rodando" sem ler o código-fonte.
- **AC-6 — Migração do Hangfire.** *Dado* um usuário de Hangfire, *então* há um guia de equivalências/migração.
- **AC-7 — Referência de API.** *Dado* um release, *então* a referência de API publicada reflete os XML docs.

## Deferred Decisions

- **DD-1 — Versão exata da licença.** ✅ **Resolvida (2026-08-04): Apache-2.0** para o core. A LGPL-3.0 foi descartada por conflitar com publicação Native AOT e por barreira de adoção corporativa. Ver [ADR-0011](../docs/adr/0011-licenca-apache-e-assinatura-de-assembly.md).
- **DD-4 — Assinatura de assembly.** ✅ **Resolvida (2026-08-04):** todos os assemblies com nome forte, chave única versionada. Decidida antes do primeiro pacote porque o token da chave entra na identidade e mudar depois quebra quem referencia.
- **DD-2 — Ferramenta de docs.** *Fallback:* DocFX (integra com XML docs .NET) ou Docusaurus. *Revisão:* setup.
- **DD-3 — CLA.** *Fallback:* adotar CLA leve (CLA Assistant) por causa do tier comercial. *Revisão:* antes de aceitar contribuições externas.
- **DD-4 — Distribuição do Pro.** *Fallback:* feed privado + chave; loja/site próprio. *Revisão:* quando o Pro for comercializado.

## Open Questions

_(vazio)_
