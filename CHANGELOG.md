# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/); versionamento
segue [SemVer](https://semver.org/lang/pt-BR/).

## [Não publicado]

Nada foi publicado no NuGet ainda. Esta seção acumula o que entra na primeira release.

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

### Alterado

- Licença do core passou de LGPL-3.0 para **Apache-2.0**, e todos os assemblies passaram a
  ser assinados com nome forte.
