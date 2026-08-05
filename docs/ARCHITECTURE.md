# Arquitetura do Guará

> Guará é um framework de agendamento e execução de tarefas (job scheduler) orientado a **componentes**, inspirado em ASP.NET Core, Entity Framework Core, Hangfire e MediatR.

Este documento é o **hub da arquitetura**. Ele define os princípios e aponta para os documentos de aprofundamento. Toda decisão de design deve ser rastreável até aqui ou até um [ADR](adr/README.md).

## Índice da Documentação

| Documento | Conteúdo |
|---|---|
| **ARCHITECTURE.md** (este) | Filosofia, regras de dependência, estrutura, nomenclatura, fluxos, performance, checklist — visão geral |
| [components.md](components.md) | Catálogo de componentes e responsabilidade única de cada um |
| [dependency-rules.md](dependency-rules.md) | Regras de dependência em profundidade + camadas |
| [naming-conventions.md](naming-conventions.md) | Convenções de nomenclatura de tipos, pacotes, namespaces e extensões |
| [execution-flows.md](execution-flows.md) | Ciclo de vida do Job, fluxo de eventos e pipeline de middlewares |
| [semantics.md](semantics.md) | **Garantias semânticas**: entrega at-least-once, ordem, precisão, retentativas, cancelamento/tempo limite, recorrentes (sobreposição/misfire), exclusão, filas |
| [reference/](reference/README.md) | **Referência competitiva** (Hangfire × Quartz.NET): como cada funcionalidade é implementada nas duas ferramentas + matriz de paridade, para guiar a implementação do Guará |
| [patterns.md](patterns.md) | Padrões obrigatórios: componente, API fluente, extensão de DI, middleware, provider |
| [anti-patterns.md](anti-patterns.md) | O que **nunca** fazer no Guará |
| [performance.md](performance.md) | Princípios de performance e regras de alocação |
| [checklist.md](checklist.md) | Checklist obrigatório antes de commitar um novo componente |
| [adr/README.md](adr/README.md) | Architecture Decision Records |

---

## 1. Filosofia Arquitetural

O Guará **não** é organizado por camadas tradicionais (`Controllers → Services → Repositories`) nem por DDD clássico. A **unidade arquitetural é o componente**.

Cada componente representa **uma responsabilidade única** e possui seu próprio ciclo de vida, abstrações, implementações, testes e documentação. Componentes evoluem de forma independente e se compõem via contratos.

### Valores inegociáveis

| Valor | Significado prático |
|---|---|
| **Alta coesão** | Um projeto = uma responsabilidade. Tudo que muda junto vive junto. |
| **Baixo acoplamento** | Componentes se conhecem apenas por interfaces em `Guara.Abstractions`. |
| **Extensibilidade** | Novos providers/comportamentos entram sem tocar no núcleo. |
| **Performance** | Zero reflection em runtime, baixa alocação, AOT-friendly. Ver [performance.md](performance.md). |
| **Evolução independente** | Cada pacote versiona e publica sozinho. |
| **API fluente** | Configuração legível, inspirada em ASP.NET Core (`AddGuara().UseSqlServerStorage()`). |
| **Inversão de dependência** | Todo componente depende de abstrações, nunca de implementações. |
| **Composição sobre herança** | Comportamento é montado via middlewares e DI, não via hierarquias de classe. |

### Frameworks de referência

- **ASP.NET Core** → modelo de Hosting, pipeline de middlewares, `Add*/Use*`, namespace `Microsoft.Extensions.DependencyInjection`.
- **EF Core** → abstração de provider (`IStorage` com implementações intercambiáveis).
- **Hangfire** → domínio do problema (jobs, recurring, dashboard, retry).
- **MediatR** → comunicação desacoplada por mensagens/eventos e pipeline behaviors.

---

## 2. Regras de Dependência

> Detalhe completo em [dependency-rules.md](dependency-rules.md).

Dependências são **unidirecionais**. Nunca o contrário.

```
Dashboard  →  Api  →  Core  →  Abstractions
```

Três leis:

1. **Um projeto = uma responsabilidade.** Nunca misturar (ex.: `Storage + Scheduler`, `Scheduler + Worker`).
2. **Componentes só conhecem interfaces.** `IStorage`, `IScheduler`, `IWorker`, `IExecutor`, `ISerializer`, `ILockProvider` — nunca a classe concreta.
3. **Nenhum componente conversa diretamente com outro.** Toda comunicação passa por contrato ou evento. `Scheduler` nunca chama `SqlServerStorage`; chama `IStorage`.

```
Scheduler  →  IStorage  →  SqlServerStorage
   (nunca Scheduler → SqlServerStorage)
```

`Guara.Abstractions` é o topo da pirâmide invertida: **não depende de ninguém** e é referenciado por todos. `Guara.Core` conhece apenas `Abstractions` — **nunca** banco, ASP.NET ou Dashboard.

---

## 3. Estrutura de Pacotes

> Catálogo e responsabilidade de cada componente em [components.md](components.md).

```
Guara.sln
├── src/
│   ├── Guara.Abstractions          # contratos puros — não depende de nada
│   ├── Guara.Core                  # modelos internos, estados, pipeline, abstrações comuns
│   ├── Guara.Hosting               # AddGuara(), DI, bootstrap, HostedServices
│   ├── Guara.Server                # lifecycle: inicia workers/scheduler, heartbeat
│   │
│   ├── Guara.Scheduler             # cálculo de Cron/Delay/Recurring/NextRun (não executa)
│   ├── Guara.Worker                # apenas executa Jobs
│   ├── Guara.Dispatcher            # apenas busca Jobs (não executa, não agenda)
│   ├── Guara.Executor              # recebe Job pronto, executa, atualiza estado, finaliza
│   │
│   ├── Guara.Storage               # IStorage, ITransaction, ILockProvider, IQueueStorage... (não implementa)
│   ├── Guara.Storage.Memory        # implementação in-memory
│   ├── Guara.Storage.SqlServer     # implementação SQL Server
│   ├── Guara.Storage.PostgreSql    # implementação PostgreSQL
│   ├── Guara.Storage.MySql         # implementação MySQL
│   ├── Guara.Storage.Mongo         # implementação MongoDB
│   │
│   ├── Guara.Redis                 # acelerador: leva o aviso de fila entre nós (não é storage)
│   │
│   ├── Guara.Serialization         # somente serialização
│   ├── Guara.Diagnostics           # logging, metrics, tracing, healthchecks
│   ├── Guara.OpenTelemetry         # exporters OTel
│   │
│   ├── Guara.Authorization         # autorização de jobs/dashboard
│   ├── Guara.Authentication        # autenticação
│   │
│   ├── Guara.Dashboard             # composição do dashboard
│   ├── Guara.Dashboard.Api         # APIs do dashboard (nunca renderiza HTML)
│   ├── Guara.Dashboard.Angular     # SPA Angular (só consome API)
│   │
│   ├── Guara.Configuration         # binding de opções
│   ├── Guara.Extensions            # extensões utilitárias
│   │
│   ├── Guara.Cluster               # leader election, heartbeat, node discovery, failover, locks distribuídos
│   ├── Guara.Distributed           # coordenação distribuída
│   │
│   ├── Guara.Cli                   # ferramenta de linha de comando
│   ├── Guara.Analyzers             # analisadores Roslyn (enforçam as regras deste doc)
│   └── Guara.SourceGenerators      # descoberta e registro sem reflection
│
├── tests/                          # um projeto de teste por componente
├── samples/                        # exemplos de uso
├── benchmarks/                     # BenchmarkDotNet por componente crítico
└── docs/                           # esta documentação
```

Regra: **`Guara.Storage` define, `Guara.Storage.*` implementa.** O mesmo vale para todo ponto de extensão (provider). Ver [ADR-0003](adr/0003-abstracao-de-storage-por-provider.md).

---

## 4. Convenções de Nomenclatura

> Detalhe completo em [naming-conventions.md](naming-conventions.md).

| Elemento | Convenção | Exemplo |
|---|---|---|
| Interface principal do componente | `I{Componente}` | `IWorker`, `IScheduler`, `IStorage`, `IExecutor` |
| Provider (implementação) | `{Tecnologia}{Contrato}` | `SqlServerStorage`, `MySqlLockProvider`, `RedisQueueSignal` |
| Pacote | `Guara.{Componente}[.{Provider}]` | `Guara.Storage.PostgreSql` |
| Extensão de DI | **um único** `AddGuara...()` / `Use...()` por pacote | `AddGuara()`, `UseSqlServerStorage()` |
| Namespace das extensões | `Microsoft.Extensions.DependencyInjection` | integra-se ao ecossistema .NET |
| Evento | `{Substantivo}{ParticípioPassado}` | `JobCreated`, `JobScheduled`, `JobCompleted` |
| Opções | `{Componente}Options` | `SchedulerOptions`, `WorkerOptions` |
| Middleware | `{Etapa}Middleware` | `RetryMiddleware`, `MetricsMiddleware` |

Regra de ouro: **toda API pública deve ser pequena.** Cada pacote expõe **um** método `AddGuara...()`.

---

## 5. Fluxos de Execução

> Detalhe completo em [execution-flows.md](execution-flows.md).

**Comunicação por eventos** — nenhum componente chama outro diretamente:

```
JobCreated → Scheduler → JobScheduled → Dispatcher → WorkerRequested
           → ExecutorStarted → JobCompleted
```

**Pipeline do Job** — cada etapa é um middleware (modelo ASP.NET Core):

```
Validation → Authorization → Serialization → Middleware
           → Metrics → Logging → Retry → Executor → Success → Notifications
```

Filas internas usam `Channel<T>`; toda API assíncrona propaga `CancellationToken`. Ver [ADR-0002](adr/0002-comunicacao-por-eventos.md) e [ADR-0004](adr/0004-channel-para-filas-internas.md).

---

## 6. Princípios de Performance

> Detalhe completo em [performance.md](performance.md). Cada componente **deve** respeitar:

- **Zero reflection em runtime** sempre que possível — descoberta/registro via [Source Generators](adr/0005-source-generators-para-registro.md).
- `Channel<T>` para filas internas.
- `ValueTask` em APIs de caminho crítico.
- `Span<T>` / `Memory<T>` quando aplicável.
- **Object Pool** para objetos de curta duração.
- Baixa alocação de memória; processamento assíncrono com `CancellationToken` completo.
- **Thread safety por padrão.**
- Compatibilidade com **Native AOT** e **Trimming**. Ver [ADR-0008](adr/0008-native-aot-e-trimming.md).

Todo componente crítico tem um projeto correspondente em `benchmarks/`.

---

## 7. ADRs — Architecture Decision Records

Decisões arquiteturais são registradas e versionadas em [adr/](adr/README.md). Nenhuma decisão estrutural entra no código sem um ADR correspondente.

| ADR | Decisão |
|---|---|
| [0001](adr/0001-arquitetura-orientada-a-componentes.md) | Arquitetura orientada a componentes (não camadas, não DDD clássico) |
| [0002](adr/0002-comunicacao-por-eventos.md) | Comunicação por eventos; nenhum componente chama outro diretamente |
| [0003](adr/0003-abstracao-de-storage-por-provider.md) | Abstração de Storage por provider (`IStorage` + `Guara.Storage.*`) |
| [0004](adr/0004-channel-para-filas-internas.md) | `Channel<T>` para filas internas |
| [0005](adr/0005-source-generators-para-registro.md) | Source Generators para descoberta/registro (zero reflection) |
| [0006](adr/0006-uma-extensao-addguara-por-pacote.md) | Um único `AddGuara...()` por pacote, em `Microsoft.Extensions.DependencyInjection` |
| [0007](adr/0007-pipeline-de-middlewares.md) | Pipeline de middlewares para execução de Jobs |
| [0008](adr/0008-native-aot-e-trimming.md) | Compatibilidade com Native AOT e Trimming |
| [0009](adr/0009-politica-de-dependencias.md) | Política de dependências (núcleo sem terceiros; drivers isolados; cron próprio) |
| [0010](adr/0010-api-do-usuario-em-portugues.md) | API voltada ao usuário em português (métodos do `IGuaraClient`) |
| [0011](adr/0011-licenca-apache-e-assinatura-de-assembly.md) | Core sob Apache-2.0 e assemblies com nome forte |
| [0012](adr/0012-wakeup-por-sinal-de-fila.md) | Wakeup por sinal de fila (`IQueueSignal`), com o polling como piso |
| [0013](adr/0013-redis-como-acelerador.md) | Redis como acelerador (`Guara.Redis`), não como storage |

---

## 8. Checklist Obrigatório para Novos Componentes

> Versão completa e acionável em [checklist.md](checklist.md). Resumo:

- [ ] Uma única responsabilidade — nada misturado.
- [ ] Interface principal em `Guara.Abstractions`; implementação no pacote do componente.
- [ ] Depende **apenas** de abstrações; nenhuma dependência de implementação concreta.
- [ ] Comunicação por evento/contrato — nunca chamada direta a outro componente.
- [ ] Um único `AddGuara...()` / `Use...()` no namespace `Microsoft.Extensions.DependencyInjection`.
- [ ] `Options` dedicado + validação de configuração.
- [ ] Zero reflection em runtime; APIs críticas com `ValueTask` e `CancellationToken`.
- [ ] AOT/Trimming-safe.
- [ ] Projeto de testes correspondente em `tests/`; benchmark em `benchmarks/` se for caminho crítico.
- [ ] ADR criado se a decisão for estrutural.

---

## 9. Distribuição e Licença

O Guará é um **produto open-source publicado no NuGet** (como Hangfire/EF Core), com um tier comercial. Detalhes nas specs [033](../spec/033-empacotamento-build-versionamento.md)–[035](../spec/035-governanca-licenciamento-docs.md).

- **Target Frameworks:** multi-target `net8.0` (LTS) + `net10.0`. AOT/trimming plenos no `net10`; features específicas sob `#if NET10_0_OR_GREATER`.
- **Licença:** core **Apache-2.0** (aberto, com concessão de patente e sem atrito com AOT/single-file); pacotes `Guara.Pro.*` sob **licença comercial** (ex.: `Guara.Pro.Batches`). O core **nunca** referencia pacotes Pro. Ver [ADR-0011](adr/0011-licenca-apache-e-assinatura-de-assembly.md).
- **Assinatura:** todos os assemblies com **nome forte** (chave única `guara.snk`) — identidade de binding, não segurança.
- **Empacotamento:** Central Package Management, `.slnx`, SourceLink, símbolos `.snupkg`, XML docs, versão semântica automática, `PublicApiAnalyzers` (extend-only).
- **Release:** CI multi-TFM + matriz AOT + conformance de providers (Testcontainers); publish no NuGet.org por tag.

