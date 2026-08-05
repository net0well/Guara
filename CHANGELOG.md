# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/); versionamento
segue [SemVer](https://semver.org/lang/pt-BR/).

## [Não publicado]

Nada desde o preview.

## [0.1.0-preview.1] — 2026-08-04

Primeiro preview público no NuGet: 18 pacotes. Por ser pré-lançamento, exige
`--prerelease` na instalação e a API pública ainda pode mudar até o 1.0.

### Adicionado

- Núcleo do agendador: abstrações, pipeline de middlewares, máquina de estados, hosting,
  servidor com heartbeat e os motores de scheduler, dispatcher, worker e executor.
- Providers de storage em memória e PostgreSQL, com kit de conformidade compartilhado.
- Agendamento fluente com cron próprio, intervalos com janela diária, fusos IANA/Windows
  nativos e calendários de exclusão.
- Continuações, atributos declarativos de job e source generators para registro sem
  reflection.
- Painel web: API v1 com stream SSE, busca com filtros, séries temporais, gestão de
  recorrentes, CRUD de calendários e ações em massa.
- SPA Angular do painel: visão geral com gráficos ao vivo, jobs, detalhe, recorrentes,
  calendários e servidores — com tema claro/escuro e i18n pt-BR/en.
- `Guara.Authorization`: permissões por ação do painel, negadas por omissão.
- Diagnóstico com `ILogger`, métricas e tracing nativos.

### Infraestrutura

- Licença **Apache-2.0** no core (a escolha original era LGPL-3.0, trocada por conflitar com
  publicação Native AOT), assemblies com **nome forte** e chave única.
- Superfície pública congelada com `PublicApiAnalyzers`: 809 assinaturas, com implementação
  fechada como `internal` e alcançada por contrato.
- Empacotamento com versão derivada da tag (MinVer), SourceLink, símbolos `.snupkg`, README e
  ícone dentro de cada pacote.
- CI com build multi-TFM, conformidade de storage em container, análise CodeQL e publicação
  por tag via Trusted Publishing.

[Não publicado]: https://github.com/net0well/Guara/compare/v0.1.0-preview.1...HEAD
[0.1.0-preview.1]: https://github.com/net0well/Guara/releases/tag/v0.1.0-preview.1
