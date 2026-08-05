<p align="center">
  <img src="assets/logo-escrita.png" alt="Guará — Job Scheduler" width="440">
</p>

<p align="center">
  <strong>Agendamento e execução de jobs para .NET moderno — orientado a componentes, agnóstico a storage, pronto para AOT.</strong>
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/Guara.Hosting"><img src="https://img.shields.io/nuget/vpre/Guara.Hosting?label=NuGet&color=004880" alt="NuGet"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/licen%C3%A7a-Apache--2.0-blue" alt="Licença: Apache-2.0"></a>
  <img src="https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4" alt=".NET 8.0 | 10.0">
  <img src="https://img.shields.io/badge/status-em%20desenvolvimento%20ativo-orange" alt="Status: em desenvolvimento ativo">
</p>

<p align="center">
  Português (Brasil) | <a href="README.en.md">English</a>
</p>

---

> **Status do projeto.** O Guará está em desenvolvimento ativo, com o primeiro **preview público no NuGet** (`0.1.0-preview.1`). O runtime, o storage PostgreSQL e o painel completo estão implementados e cobertos por testes. Por ser pré-lançamento, a API pública ainda pode mudar até o 1.0 — o que não existe está marcado como _planejado_ ao longo deste documento e no [roadmap](#roadmap). Dê star e acompanhe o repositório para seguir o progresso.

## Instalação

Pré-lançamento exige a flag `--prerelease`; sem ela o NuGet ignora a versão:

```bash
dotnet add package Guara.Hosting --prerelease              # ponto de entrada: AddGuara()
dotnet add package Guara.Server --prerelease               # executa os jobs neste processo
dotnet add package Guara.Storage.PostgreSql --prerelease   # storage (escolha um)
dotnet add package Guara.Dashboard --prerelease            # opcional: painel web
```

Para desenvolvimento e testes, troque o storage por `Guara.Storage.Memory`.

## Pacotes

Instale só o que usar — o núcleo roda sozinho e todo o resto é opcional. A coluna de estado separa o que já está publicado do que ainda vai sair.

| Pacote | Para quê | Estado |
|---|---|---|
| `Guara.Hosting` | Ponto de entrada: `AddGuara()` e o builder fluente | ✅ publicado |
| `Guara.Server` | Lifecycle: workers, scheduler, heartbeat, manutenção | ✅ publicado |
| `Guara.Scheduler` | Cron próprio, recorrentes, calendários, `IGuaraClient` | ✅ publicado |
| `Guara.Storage.PostgreSql` | Storage PostgreSQL — recomendado para produção | ✅ publicado |
| `Guara.Storage.Memory` | Storage em memória — dev, testes e demos | ✅ publicado |
| `Guara.Dashboard` | Painel web (API + SPA Angular embutida, tempo real) | ✅ publicado |
| `Guara.Authorization` | Permissões por ação do painel | ✅ publicado |
| `Guara.Diagnostics` | Logs estruturados, métricas e traces | ✅ publicado |
| `Guara.SourceGenerators` | Registro e invocação de jobs sem reflection | ✅ publicado |
| `Guara.Abstractions` / `Guara.Storage` | Contratos — para autores de providers e extensões | ✅ publicado |
| `Guara.Storage.SqlServer` | Storage SQL Server 2016+ | 🟡 implementado, sai no próximo preview |
| `Guara.Storage.MySql` | Storage MySQL 8+ | 🟡 implementado, sai no próximo preview |
| `Guara.Storage.Mongo` | Storage MongoDB | 🕓 planejado |
| `Guara.Storage.Redis` | Storage Redis | 🕓 planejado |
| `Guara.Authentication` | Esquemas de autenticação (JWT, OIDC, cookie) | 🕓 planejado |
| `Guara.Cluster` / `Guara.Distributed` | Eleição de líder, failover, coordenação distribuída | 🕓 planejado |
| `Guara.OpenTelemetry` | Exporters OpenTelemetry | 🕓 planejado |
| `Guara.Cli` | Ferramenta de linha de comando (`dotnet tool`) | 🕓 planejado |
| `Guara.Analyzers` | Analisadores Roslyn que enforçam as regras de dependência | 🟡 implementado, sai no próximo preview |
| `Guara.Pro.Batches` | Comercial: grupos de jobs com callback de conclusão | 🕓 planejado |

## O que é o Guará

O Guará é um **job scheduler open source para .NET**, no mesmo espaço de problema do Hangfire: jobs fire-and-forget, execução com atraso, jobs recorrentes por cron, retentativas automáticas, processamento distribuído entre nós e um dashboard em tempo real para observar e operar tudo.

O que o torna diferente é a forma como é construído:

- **Arquitetura orientada a componentes.** Não há camadas tradicionais. Cada responsabilidade — agendar, buscar, executar, armazenar, observar — é um componente independente com contratos, pacote, testes e ciclo de vida próprios. Componentes se comunicam apenas por interfaces e eventos, nunca referenciando implementações uns dos outros.
- **O storage é a fila.** Nenhum message broker é necessário. Os jobs vivem no seu banco de dados, e a aquisição atômica com lease (visibility timeout) garante que cada job seja processado por exatamente um worker, mesmo com muitos nós.
- **Zero dependências de terceiros no núcleo.** O runtime depende apenas da plataforma .NET (`Microsoft.Extensions.*`, `System.Text.Json`). Drivers de banco ficam isolados nos pacotes de provider. Até o parser de cron é nosso.
- **Zero reflection, pronto para Native AOT.** Descoberta, registro e invocação de jobs — inclusive a leitura dos atributos — são gerados em tempo de compilação por source generators. O núcleo compila limpo sob trimming e `PublishAot`.
- **Logging estruturado sem lock-in.** O Guará loga via `Microsoft.Extensions.Logging` com propriedades estruturadas (`JobId`, `Queue`, `Attempt`, `State`...). Plugue o sink que quiser — JSON console, OpenTelemetry, Seq, ELK — o framework não impõe nada.
- **Tudo é opcional além do núcleo.** Dashboard, OpenTelemetry, cluster: pacotes separados. Instale só o que usar.

O nome vem do **lobo-guará**, animal veloz e resiliente nativo do Brasil. Made in Brasil.

## Recursos

| Capacidade | Descrição |
|---|---|
| Fire-and-forget | Enfileire um job e retorne imediatamente |
| Jobs com atraso | Execute uma vez após um intervalo |
| Jobs recorrentes | Expressões cron ou intervalos, com **builder fluente** estilo Quartz (`ComId`, `IniciaEm`, `ComCalendario`...) |
| Calendários | Datas e janelas excluídas (feriados, fins de semana) reutilizáveis entre recorrentes — gerenciáveis por código ou pelo dashboard |
| Fuso horário nativo | Ids IANA (`America/Sao_Paulo`) e Windows aceitos nos dois sistemas — sem pacotes de terceiros |
| Continuations | Encadeie jobs: B roda automaticamente quando A termina |
| Retentativas automáticas | Back-off exponencial, configurável por job, com opt-out para trabalho não idempotente |
| Atributos declarativos | `[GuaraFila]`, `[GuaraRetentativas]`, `[GuaraDesabilitarConcorrencia]`, `[GuaraTempoLimite]`... — comportamento declarado no próprio job |
| Dashboard em tempo real | SPA Angular alimentada por Server-Sent Events — mudanças de estado aparecem em cerca de um segundo. **Opcional**: pacote separado |
| Painel operável | Busca por texto/tipo/fila/estado/período, gráficos ao vivo de vazão e latência (p50/p95), gestão de recorrentes (pausar, retomar, disparar, editar agenda), edição de calendários em visão mensal e ações em massa |
| Autenticação do dashboard | Regras fluentes (logados, papéis, claims, IPs internos, combináveis), regra customizada com `HttpContext`, login fixo e **página de login própria** com a identidade do Guará |
| Permissões granulares | Cada ação do painel exige sua concessão (`guara:view`, `guara:retry`, `guara:trigger`, `guara:delete`, `guara:calendars`, `guara:view-payload`), sobre as policies do ASP.NET Core |
| Storage plugável | Contratos comuns com kit de conformidade que todo provider herda. Hoje: PostgreSQL, SQL Server, MySQL e In-Memory — troque com uma linha |
| Observabilidade | Logs estruturados, métricas (`System.Diagnostics.Metrics`), traces (`ActivitySource`) |
| Seguro por padrão | O dashboard nega acesso anônimo a menos que configurado explicitamente; o que não foi concedido é negado |
| _Planejado_ | Processamento distribuído (eleição de líder, failover), demais providers de storage, exporters OpenTelemetry e CLI |

## Começando

Com os pacotes instalados (ver [Instalação](#instalação)), configure tudo com uma API fluente, no estilo do ASP.NET Core:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddGuara()
    .UsePostgreSqlStorage(builder.Configuration.GetConnectionString("guara")!)
    .AddGuaraServer()        // inicia workers, scheduler, heartbeat
    .AddGuaraDashboard();    // OPCIONAL — remova e o Guará roda sem painel

var app = builder.Build();

app.MapGuaraDashboard();     // OPCIONAL — serve a SPA + API em /guara
app.Run();
```

Também funciona num Worker Service puro (sem ASP.NET Core), para processos dedicados de processamento:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddGuara()
    .UsePostgreSqlStorage(builder.Configuration.GetConnectionString("guara")!)
    .AddGuaraServer();

builder.Build().Run();
```

### Enfileirando jobs

Enfileire de qualquer lugar através do `IGuaraClient`.

> **API em português.** O Guará é um projeto brasileiro e os métodos de operação de jobs são em português, por decisão de identidade ([ADR-0010](docs/adr/0010-api-do-usuario-em-portugues.md)). O restante (tipos, extensões de DI, options, rotas) segue as convenções do ecossistema .NET em inglês.

```csharp
public sealed class RelatorioService(IGuaraClient jobs)
{
    public async Task SolicitarAsync(int clienteId, CancellationToken ct)
    {
        // Fire-and-forget: roda assim que houver worker livre
        await jobs.EnfileirarAsync(() => GerarRelatorioAsync(clienteId), ct);

        // Com atraso: roda uma vez, daqui a 24 horas
        await jobs.AgendarAsync(() => EnviarLembreteAsync(clienteId), TimeSpan.FromHours(24), ct);
    }

    public Task GerarRelatorioAsync(int clienteId) { /* ... */ }
    public Task EnviarLembreteAsync(int clienteId) { /* ... */ }
}
```

### Jobs recorrentes (builder fluente, estilo Quartz)

Recorrentes são configurados com um **builder fluente** ([spec 038](spec/038-agendamento-fluente.md)) — identidade, agenda, vigência, descrição e calendário num só lugar:

```csharp
await jobs.AdicionarOuAtualizarRecorrenteAsync(job => job
    .ComId("limpeza-noturna")
    .Executa(() => LimparRegistrosExpiradosAsync())
    .ComCron("0 3 * * *")                                          // todo dia às 03:00
    .NoFusoHorario("America/Sao_Paulo")                            // aceita id IANA ou Windows
    .IniciaEm(GuaraDatas.SegundoExato(DateTimeOffset.UtcNow.AddSeconds(7)))
    .ComDescricao("Remove registros expirados da base")
    .ComCalendario("feriados"),                                    // pula datas excluídas
    ct);

// Agenda por intervalo (sem cron), com janela diária
await jobs.AdicionarOuAtualizarRecorrenteAsync(job => job
    .ComId("sincronizacao-precos")
    .Executa(() => SincronizarPrecosAsync())
    .ACada(TimeSpan.FromSeconds(10))
    .EntreHorarios(new TimeOnly(8, 0), new TimeOnly(18, 0)),
    ct);
```

`GuaraDatas` é o construtor de datas de disparo (equivalente ao `DateBuilder` do Quartz): `SegundoExato`, `HojeAs(3, 0)`, `AmanhaAs(8, 0)`, `ProximoDiaUtil()`... A conversão de fuso Windows↔Linux (IANA↔Windows) é **nativa e automática** — sem pacotes de terceiros.

### Calendários (feriados e janelas excluídas)

Calendários são persistidos e reutilizáveis por vários recorrentes; alterar um calendário **recalcula automaticamente** o próximo disparo de quem o usa:

```csharp
await jobs.AdicionarOuAtualizarCalendarioAsync("feriados", cal => cal
    .ExcluirData(new DateOnly(2026, 12, 25))
    .ExcluirData(new DateOnly(2027, 1, 1))
    .ExcluirDiasDaSemana(DayOfWeek.Sunday),
    ct);
```

Os calendários também podem ser criados e mantidos **pela interface do dashboard** — uma visão mensal leve para adicionar feriados e excluir datas — com o mesmo efeito: os recorrentes que os usam recalculam automaticamente, venha a mudança do código ou do painel.

### Continuations e exclusão

```csharp
// B roda automaticamente quando A concluir com sucesso
var exportacao = await jobs.EnfileirarAsync(() => ExportarPedidosAsync(mes), ct);
await jobs.ContinuarComAsync(exportacao, () => NotificarExportacaoConcluidaAsync(mes), ct);

// Cancelar/excluir um job que ainda não rodou
await jobs.ExcluirAsync(exportacao, ct);
```

| Método | Significado |
|---|---|
| `EnfileirarAsync` | Fire-and-forget |
| `AgendarAsync` | Executar uma vez após um atraso |
| `AdicionarOuAtualizarRecorrenteAsync` | Criar/atualizar job recorrente (upsert) |
| `ContinuarComAsync` | Continuation (encadear) |
| `ExcluirAsync` | Excluir |

## Atributos de job

Comportamento declarado **no próprio job** — em português, como toda a API do usuário ([spec 036](spec/036-atributos-de-job.md)). Os atributos são lidos em **tempo de compilação** pelo source generator: zero reflection em runtime.

```csharp
public sealed class RelatorioJobs(IRelatorioService servico)
{
    [GuaraJob]                                          // descoberta (gerada em compilação)
    [GuaraFila("relatorios")]                           // fila dedicada
    [GuaraRetentativas(5)]                              // até 5 retentativas com back-off
    [GuaraDesabilitarConcorrencia(Chave = "cliente-{0}")] // nunca 2 execuções para o mesmo cliente
    [GuaraTempoLimite(300)]                             // cancela após 5 minutos
    public Task GerarAsync(int clienteId, CancellationToken ct)
        => servico.GerarAsync(clienteId, ct);

    [GuaraJob]
    [GuaraRetentativas(0)]                              // nunca retentar: cobrar 2x é pior que falhar
    public Task CobrarClienteAsync(int faturaId, CancellationToken ct)
        => servico.CobrarAsync(faturaId, ct);

    [GuaraJob]
    [GuaraPularSeAnteriorEmExecucao]                    // recorrente: não acumula se a anterior ainda roda
    public Task SincronizarCatalogoAsync(CancellationToken ct)
        => servico.SincronizarAsync(ct);
}
```

| Atributo | Efeito |
|---|---|
| `[GuaraFila("nome")]` | Define a fila do job |
| `[GuaraRetentativas(n)]` | Política de retentativa por job; `0` desliga |
| `[GuaraDesabilitarConcorrencia]` | Exclusão mútua por chave, mesmo entre nós (equivalente ao `DisableConcurrentExecution` do Hangfire) |
| `[GuaraTempoLimite(segundos)]` | Tempo máximo de execução; excedido → cancelamento cooperativo |
| `[GuaraPularSeAnteriorEmExecucao]` | Recorrentes: pula a ocorrência se a anterior ainda executa |

## Como funciona

Cada componente reage a um evento e emite o próximo — nenhum componente chama outro diretamente:

```
JobCreated -> Scheduler -> JobScheduled -> Dispatcher -> WorkerRequested
           -> Worker -> ExecutorStarted -> Executor -> JobCompleted | JobFailed
```

Dentro do executor, cada job percorre um pipeline de middlewares, no modelo do ASP.NET Core:

```
Validation -> Authorization -> Serialization -> Middleware custom
           -> Metrics -> Logging -> Retry -> Execution -> Success -> Notifications
```

Cada etapa é um `IJobMiddleware`; o slot `Custom` é o ponto de extensão do usuário. A arquitetura completa — regras de dependência, convenções de nomenclatura, fluxos de execução e os ADRs por trás de cada decisão — está documentada em [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

### Regras de arquitetura que quebram a build

As regras de dependência não vivem só na documentação: `Guara.Analyzers` as transforma em erro de compilação, e todo pacote do Guará compila com ele ligado.

| Regra | O que impede |
|---|---|
| `GUARA0001` | Dependência invertida — um componente referenciando outro de camada superior |
| `GUARA0002` | Motor de execução alcançando um provider concreto em vez do contrato |

## Providers de storage

O `Guara.Storage` define os contratos; cada provider os implementa usando as melhores primitivas do seu backend. Todos os providers precisam passar o mesmo **kit de testes de conformidade** (aquisição atômica sob concorrência, lease/visibility, idempotência, locks com TTL).

| Provider | Dequeue atômico | Lock distribuído | Estado |
|---|---|---|---|
| PostgreSQL | `FOR UPDATE SKIP LOCKED` | Advisory locks | ✅ publicado, conformidade verde |
| In-Memory | Exclusão mútua sobre o dicionário | Local ao processo | ✅ publicado, conformidade verde |
| SQL Server 2016+ | `READPAST + UPDLOCK` com `OUTPUT` | Tabela com TTL e dono | 🟡 conformidade verde, sai no próximo preview |
| MySQL 8+ | `FOR UPDATE SKIP LOCKED` | Tabela com TTL e dono | 🟡 conformidade verde, sai no próximo preview |
| MongoDB | `findAndModify` | Coleção com TTL | 🕓 planejado |
| Redis | Scripts Lua | `SET NX PX` + TTL | 🕓 planejado (escopo em revisão) |

Cada provider isola suas tabelas do resto do banco. PostgreSQL e SQL Server usam um **schema** dedicado (`Schema`, padrão `guara`); no MySQL schema e banco de dados são a mesma coisa, então o isolamento é por **prefixo de tabela** (`TablePrefix`, padrão `guara_`).

Trocar de provider é uma mudança de uma linha:

```csharp
builder.Services.AddGuara().UseMemoryStorage();                    // dev/testes
builder.Services.AddGuara().UsePostgreSqlStorage(connectionString); // produção
builder.Services.AddGuara().UseSqlServerStorage(connectionString);  // produção
builder.Services.AddGuara().UseMySqlStorage(connectionString);      // produção
```

## Dashboard (opcional)

O dashboard **não é obrigatório**: o núcleo do Guará roda sozinho e o painel é um pacote separado (`Guara.Dashboard`) — instale apenas se quiser a interface web. É uma SPA Angular servida como assets estáticos embutidos — sem deploy separado, sem Node.js em runtime. Consome apenas a API HTTP versionada (`/guara/api/v1`) e atualiza em tempo real por Server-Sent Events. **Acesso anônimo é negado por padrão.**

### Autenticação do painel

Proteger o painel não exige implementar filtro nenhum na mão ([spec 037](spec/037-dashboard-autenticacao.md)) — as regras comuns são fluentes e **combináveis** (todas precisam passar; use `QualquerUma(...)` para "ou"):

```csharp
builder.Services
    .AddGuara()
    .AddGuaraDashboard(dash => dash
        .UseGuaraAuthentication(auth => auth
            .PermitirApenasLogados()                 // exige usuário autenticado
            .ExigirPapel("Admin")                    // apenas administradores
            .ExigirClaim("departamento", "ti")       // verifica uma claim
            .PermitirApenasIpsInternos()));          // só rede interna/loopback
```

Para cenários simples (rede interna, homologação), há **login e senha fixos** — com página de login própria, com a logo do Guará, tema claro/escuro e proteção contra força bruta:

```csharp
.UseGuaraAuthentication(auth => auth
    .ComLoginFixo(
        usuario: "guara_admin",
        senha: builder.Configuration["Guara:Dashboard:Senha"]!)   // via env/secret, nunca literal
    .PermitirApenasIpsInternos());
```

E quando as regras embutidas não bastam, implemente `IDashboardAccessRule` — o equivalente (mais limpo) do `IDashboardAuthorizationFilter` do Hangfire, com acesso completo ao `HttpContext`:

```csharp
public sealed class SomenteHorarioComercial : IDashboardAccessRule
{
    public ValueTask<bool> AutorizarAsync(DashboardContext contexto, CancellationToken ct)
    {
        var http = contexto.HttpContext;             // = GetHttpContext() do Hangfire
        var hora = TimeProvider.System.GetLocalNow().Hour;
        return ValueTask.FromResult(
            contexto.User.Identity?.IsAuthenticated == true && hora is >= 8 and < 18);
    }
}

// registro: .UseGuaraAuthentication(auth => auth.ComRegra<SomenteHorarioComercial>())
```

### Permissões dentro do painel

As regras acima decidem **quem entra**. O que cada um pode **fazer** lá dentro é do `Guara.Authorization`, e é negado por omissão:

```csharp
builder.Services
    .AddGuara()
    .AddGuaraAuthorization(auth => auth
        .Require(GuaraActions.Delete, "SomenteSustentacao")   // policy do ASP.NET Core
        .AllowAll("AdministradorDoPainel"));                  // ou tudo de uma vez
```

Ações reconhecidas: `guara:view`, `guara:view-payload`, `guara:retry`, `guara:trigger`, `guara:delete` e `guara:calendars`. Sem `AddGuaraAuthorization()`, o painel segue tudo-ou-nada — quem passa pelas regras de acesso opera tudo. Com ela, cada rota exige sua concessão, vinda de uma policy do ASP.NET Core, de um papel de administrador ou de uma claim `guara:permission`.

### Operando pelo painel

Além de observar, o painel **opera**: busca por texto, tipo, fila, estado e período; gráficos ao vivo de vazão e de latência p50/p95; pausar, retomar, disparar e editar a agenda de recorrentes; criar e editar calendários numa visão mensal clicável; e retentar ou excluir jobs em massa, com o desfecho relatado item a item.

## Configuração

Toda a configuração segue o padrão Options do .NET, sob a seção `Guara` (validada no startup — configuração inválida falha no boot, não em produção):

```json
{
  "Guara": {
    "Storage": { "Provider": "PostgreSql", "ConnectionString": "..." },
    "Dispatcher": { "PollingInterval": "00:00:05", "Queues": [ "alta", "default" ] },
    "Worker": { "MaxConcurrency": 8, "ShutdownDrainTimeout": "00:00:30" },
    "Server": { "Retention": { "Succeeded": "1.00:00:00", "Failed": "7.00:00:00" } },
    "Dashboard": { "BasePath": "/guara", "RequireAuthorization": true }
  }
}
```

## Observabilidade

- **Logs**: estruturados, via `Microsoft.Extensions.Logging` — propriedades como `JobId`, `Queue`, `JobType`, `Attempt`, `DurationMs` em todo registro. O host de exemplo escreve JSON no stdout com o formatter nativo do console; use o sink que preferir.
- **Métricas**: `System.Diagnostics.Metrics` (`guara.jobs.processed`, `guara.jobs.failed`, `guara.job.duration`, `guara.queue.length`).
- **Traces**: um span de `Activity` por execução de job (`ActivitySource("Guara")`).
- **Séries temporais**: a API do painel agrega vazão, sucesso/falha e latência p50/p95 por balde, em janelas de 1h, 24h e 7 dias.
- **OpenTelemetry** _(planejado)_: um pacote opcional registrará as fontes do Guará no seu pipeline OTel. O núcleo nunca força um exporter — hoje as fontes já existem e podem ser consumidas direto.

## Roadmap

| Marco | Status |
|---|---|
| Documentação de arquitetura e ADRs | ✅ Concluído |
| Especificação completa (40 specs, uma por componente/feature) | ✅ Concluído |
| Fundação: `Guara.Abstractions` + `Guara.Core` (pipeline, máquina de estados, eventos) | ✅ Concluído |
| Serialização (`Guara.Serialization` — source-gen, allowlist) | ✅ Concluído |
| Contratos de storage + provider In-Memory + kit de conformidade | ✅ Concluído |
| Motores: Scheduler (cron próprio), Dispatcher, Worker, Executor | ✅ Concluído |
| Hosting, Server e provider PostgreSQL | ✅ Concluído |
| Continuations, atributos de job e source generators | ✅ Concluído |
| Agendamento fluente: builder, `GuaraDatas`, calendários, fusos nativos | ✅ Concluído |
| Dashboard: API v1 com SSE + SPA Angular (visão geral, jobs, recorrentes, servidores) | ✅ Concluído |
| Autenticação do painel: regras fluentes, login fixo, página de login | ✅ Concluído |
| Painel operável: busca, gráficos ao vivo, calendários, ações em massa | ✅ Concluído |
| `Guara.Authorization`: permissões por ação, negadas por omissão | ✅ Concluído |
| Licença Apache-2.0, assinatura de assembly e governança do repositório | ✅ Concluído |
| Empacotamento: versão por tag, SourceLink, símbolos, metadados de pacote | ✅ Concluído |
| Congelamento da API pública (`PublicApiAnalyzers`) | ✅ Concluído |
| CI/CD: build multi-TFM, conformance por container, publicação por tag | ✅ Concluído |
| **Primeira publicação no NuGet (`0.1.0-preview.1`)** | ✅ Concluído |
| Providers SQL Server e MySQL (mesmo kit de conformidade, 100% verde) | ✅ Concluído |
| Providers restantes: MongoDB → Redis | 🕓 Planejado |
| `Guara.Analyzers`: `GUARA0001` e `GUARA0002` ligados em todo o repositório | ✅ Concluído |
| `Guara.Extensions`, `Guara.Authentication` | 🕓 Planejado |
| Cluster e coordenação distribuída, OpenTelemetry, CLI, benchmarks | 🕓 Planejado |
| Documentação de usuário e guia de migração do Hangfire | 🕓 Planejado |
| **1.0** | 🕓 Planejado |

## Semântica e garantias

O Guará documenta suas garantias com precisão em [`docs/semantics.md`](docs/semantics.md) — leia antes de projetar seus jobs. Os pontos centrais:

- **Entrega at-least-once**: um job pode executar mais de uma vez em cenário de falha (worker morre após o trabalho, antes de persistir o estado). Jobs idempotentes são o caso ideal; para efeitos irreversíveis use `[GuaraRetentativas(0)]` e idempotência na ponta; para exclusão mútua, `[GuaraDesabilitarConcorrencia]`.
- **Cancelamento cooperativo**: efeito já ocorrido nunca é revertido; shutdown no meio da execução deixa o estado intocado e o lease garante o reprocesso.
- **Recorrentes**: ocorrências sobrepõem por padrão (como Quartz/Hangfire); misfire executa **uma** compensação ao religar; retomar um pausado não faz backfill.
- **Filas com prioridade estrita**: a ordem da lista é lei — dimensione para evitar starvation.
- **Ordem ~FIFO por fila no início da execução**, sem garantia de ordem de conclusão; precisão de disparo limitada pelo polling/push (não é tempo real).

## Documentação

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — filosofia arquitetural, regras de dependência, estrutura de pacotes, fluxos de execução, princípios de performance
- [`docs/semantics.md`](docs/semantics.md) — garantias semânticas (entrega, ordem, retentativas, cancelamento, recorrentes)
- [`docs/adr/`](docs/adr/) — Architecture Decision Records
- [`spec/`](spec/) — a especificação completa, um documento por componente, com critérios de aceite
- [`Infra/`](Infra/) — deploy Docker de referência (PostgreSQL + proxy reverso)

## Contribuindo

O Guará está sendo construído com especificação primeiro: todo componente tem uma spec aprovada com critérios de aceite antes de qualquer código. Comece por [`CONTRIBUTING.md`](CONTRIBUTING.md) — ele traz como rodar localmente, as três leis da arquitetura e o fluxo de uma mudança até o `main`. Depois leia [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) e a spec do componente que interessa.

Este projeto adota o [Contributor Covenant](CODE_OF_CONDUCT.md). Vulnerabilidade de segurança **não** vira issue pública: use o canal privado descrito em [`SECURITY.md`](SECURITY.md).

## Licença

O núcleo do Guará — tudo o que está documentado neste repositório — é licenciado sob **[Apache-2.0](LICENSE)**: uso livre em aplicações comerciais e proprietárias, sem obrigação de abrir seu código, e com concessão explícita de patente. Vale inclusive para publicação **Native AOT ou single-file**, em que a biblioteca é linkada estaticamente no seu binário.

Um pequeno conjunto de add-ons avançados (como orquestração de batches) está planejado como pacotes `Guara.Pro.*` comerciais licenciados à parte, que ajudam a financiar o desenvolvimento; o núcleo permanecerá sempre gratuito e open source. Ver [ADR-0011](docs/adr/0011-licenca-apache-e-assinatura-de-assembly.md).

Todos os assemblies são publicados com **nome forte** (assinatura de identidade de binding — não é mecanismo de segurança).
