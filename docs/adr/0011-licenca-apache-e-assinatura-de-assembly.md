# ADR-0011: Licença Apache-2.0 no core e assinatura de nome forte

**Status:** Aceito
**Data:** 2026-08-04
**Substitui:** a escolha de LGPL-3.0 registrada em [spec 035](../../spec/035-governanca-licenciamento-docs.md) (DD-1) e no [ARCHITECTURE §9](../ARCHITECTURE.md)

## Contexto

Duas decisões de distribuição precisavam ser fechadas antes do primeiro pacote sair, porque
nenhuma das duas é reversível depois de publicada:

- **Licença do core.** A escolha original era LGPL-3.0, espelhando o Hangfire.
- **Assinatura de nome forte.** O token da chave pública faz parte da identidade da
  assembly: adicionar ou remover depois quebra quem já referencia.

## Decisão

**Core sob Apache-2.0.** Os pacotes `Guara.Pro.*` seguem com licença comercial própria.

**Todos os assemblies assinados com nome forte**, com a mesma chave (`guara.snk`, versionada
no repositório).

## Justificativa

### Por que Apache-2.0 e não LGPL-3.0

1. **LGPL conflita com Native AOT.** A LGPL permite uso em software proprietário por
   linkagem dinâmica, desde que o usuário possa substituir a biblioteca. Publicação Native
   AOT ou single-file funde tudo num binário só — isso é linkagem estática, e aciona a
   obrigação de fornecer objetos ou meio de relinkar. O [ADR-0008](0008-native-aot-e-trimming.md)
   coloca AOT como característica de primeira linha do Guará: a licença criava zona cinzenta
   jurídica exatamente sobre um recurso anunciado.

2. **Bloqueio corporativo.** Políticas de dependência de muitas empresas barram qualquer
   coisa com "GPL" no nome, sem análise. Para uma biblioteca que quer ampla instalação, é
   custo de adoção puro.

3. **O copyleft não protegia o modelo de negócio.** A receita vem do tier `Guara.Pro.*`, que
   tem licença própria. Obrigar quem modifica o core a devolver as mudanças não defendia
   nada — só afastava usuário.

4. **Concessão de patente.** Apache-2.0 tem cláusula explícita de patente, que MIT não tem —
   relevante num projeto com braço comercial. É também a licença do Quartz.NET, a outra
   referência de domínio do Guará.

A troca foi feita enquanto o repositório tinha um único autor. Relicenciar depois de
contribuições externas exigiria consentimento de cada contribuidor ou um CLA desde o início.

### Por que assinar

Nome forte **não é mecanismo de segurança**: a chave viaja no repositório e qualquer um
consegue remover a assinatura e reassinar. É identidade de binding.

Entra mesmo assim porque o custo é próximo de zero, parte do mercado corporativo exige, e o
ecossistema de referência assina (EF Core, ASP.NET Core, Serilog, Hangfire). Sobretudo:
**é a decisão que não se toma depois** — nos dois sentidos, mudar quebra quem já referencia.

## Consequências

- `PackageLicenseExpression` passa a ser `Apache-2.0`; `LICENSE` traz o texto canônico e
  `NOTICE` carrega o aviso de copyright, como manda a Apache.
- `SignAssembly` vale para todos os projetos, inclusive os de teste.
- **Todo `InternalsVisibleTo` precisa nomear a chave pública da amiga** — assembly assinada
  só enxerga amiga assinada com a mesma chave. A chave vive em `$(GuaraPublicKey)` e é
  aplicada por `ItemDefinitionGroup`, para não se repetir em cada `.csproj`.
- Trocar `guara.snk` no futuro muda a identidade de todos os pacotes e é breaking change.
- Consumidores em .NET Framework passam a conseguir referenciar o Guará a partir de código
  também assinado — restrição que não existe mais no .NET moderno, mas ainda vale lá.
