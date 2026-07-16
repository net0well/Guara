# ADR-0009 — Política de Dependências

- **Status:** Aceito
- **Data:** 2026-07-16

## Contexto

O Guará é publicado no NuGet e usado por muitas aplicações. Cada dependência transitiva vira custo e risco para o consumidor: conflitos de versão, superfície de segurança maior, e atrito com Native AOT/Trimming. Precisamos de uma política explícita do que cada camada pode referenciar — senão dependências entram "por conveniência" e corroem os valores do framework.

## Decisão

1. **Núcleo sem terceiros.** Abstractions, Core, Scheduler, Dispatcher, Worker, Executor, Hosting, Server, Serialization, Diagnostics, Configuration, Extensions, Authentication, Authorization, Cluster e Distributed dependem **somente da plataforma .NET**: BCL + `Microsoft.Extensions.*` (abstractions) + `System.Text.Json`. **Zero frameworks de terceiros** — sem MediatR, AutoMapper, FluentValidation-no-núcleo, Hangfire, etc. O que precisamos do "estilo MediatR" (mediator/pipeline) é **implementação própria** (event bus + `IJobMiddleware` + source gen — Spec 002/029).

2. **Drivers de banco isolados por provider.** `Npgsql`, `Microsoft.Data.SqlClient`, `StackExchange.Redis` e `MongoDB.Driver` são permitidos **exclusivamente** dentro do respectivo `Guara.Storage.*`. Nunca vazam para o núcleo; quem não usa o provider **não paga** a dependência. Reimplementar protocolo de fio de banco seria inviável e sem valor.

3. **Cron próprio.** O agendamento cron usa um parser **próprio** atrás de `ICronParser` (Spec 005) — **sem Cronos**. Cron é um problema bem delimitado; manter próprio garante AOT e controle total (inclusive DST/timezone).

4. **Terceiros permitidos só em pacotes opt-in/à parte.** OpenTelemetry SDK isolado em `Guara.OpenTelemetry` (opt-in). Serilog aparece **apenas** no `Guara.Host` de exemplo, não no framework. Ferramentas dev/build (xUnit, Testcontainers, Verify, MinVer) **não shipam**.

5. **Evolução controlada.** Adicionar **qualquer** terceiro ao runtime exige um **novo ADR**. Verificado em revisão/CI (e passível de regra no `Guara.Analyzers`, Spec 028).

## Consequências

**Ganhos:** footprint transitivo mínimo para o consumidor; AOT/Trimming previsível; menos conflitos de versão; menor superfície de segurança; controle total do comportamento onde importa (cron).

**Custos:** mais código próprio para manter (notadamente o cron com DST) — exige testes fortes onde reimplementamos; disciplina para não "puxar um pacotinho" no núcleo.

Relaciona-se a [ADR-0005](0005-source-generators-para-registro.md) (source gen), [ADR-0008](0008-native-aot-e-trimming.md) (AOT), [Spec 005](../../spec/005-guara-scheduler.md) (cron), [Specs 011–015](../../spec/README.md) (drivers) e [Spec 033](../../spec/033-empacotamento-build-versionamento.md) (empacotamento).
