# Benchmarks — Referência (Quartz.NET) para o Guará

> **Documento de referência de implementação.** Trechos extraídos de **Quartz.NET** (Apache-2.0), citados por arquivo. O Quartz mantém uma suíte formal `Quartz.Benchmark` (BenchmarkDotNet); o **Hangfire não tem suíte de benchmark formal** aparente (checar novamente `Hangfire/tests` num passo posterior). Guia para montarmos `guara/benchmarks/`.

---

## Panorama

O Quartz usa **BenchmarkDotNet (BDN)** num projeto executável dedicado (`src/Quartz.Benchmark`) com ~15 classes de benchmark que medem **throughput** (jobs/s de ponta a ponta) e, sobretudo, **alocação de memória** (via `[MemoryDiagnoser]`) nos caminhos quentes: parsing de cron, aquisição de triggers, dispatch, thread pool, semáforos, comparadores e igualdade de chaves. A filosofia é: micro-benchmarks de componentes + macro-benchmarks de throughput do scheduler, com asserção de correção embutida (o benchmark falha se o nº de execuções não bater).

---

## Quartz.NET — a suíte `Quartz.Benchmark`

### Projeto (`src/Quartz.Benchmark/Quartz.Benchmark.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ProduceReferenceAssembly>false</ProduceReferenceAssembly> <!-- necessário p/ BDN -->
    <IsPackable>false</IsPackable>
    <SonarQubeExclude>true</SonarQubeExclude>
    <AnalysisLevel>none</AnalysisLevel>              <!-- analisadores desligados nos benchmarks -->
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Quartz.Jobs\Quartz.Jobs.csproj" />
  </ItemGroup>
</Project>
```

### Entry point (`Program.cs`)

```csharp
internal class Program
{
    private static void Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
```

`BenchmarkSwitcher` permite escolher quais benchmarks rodar via linha de comando (`--filter *Cron*`), útil para não rodar a suíte inteira toda vez.

### O que cada benchmark mede

| Benchmark | Alvo |
|---|---|
| `CronExpressionBenchmark` | Parse de expressão cron e cálculo do próximo disparo (`GetNextValidTimeAfter`) |
| `SimpleTriggerImplBenchmark` / `TriggerTimeComparatorBenchmark` | `GetFireTimeAfter` e ordenação de triggers (alocação) |
| `RAMJobStoreBenchmark` | Aquisição de triggers no store em memória |
| `SchedulerBenchmark` / `QuartSchedulerBenchmark` | **Throughput end-to-end**: N execuções com M threads/jobs/triggers, concurrent x DisableConcurrent |
| `JobDispatchBenchmark` / `JobRunShellBenchmark` / `ExecutingJobsManagerBenchmark` | Caminho de dispatch/execução de um job (alocação por execução) |
| `JobExecutionContextImplBenchmark` | Custo/alocação de montar o contexto de execução |
| `DefaultThreadPoolBenchmark` / `SimpleSemaphoreBenchmark` | Thread pool e semáforo (primitivas de concorrência) |
| `KeyBenchmark` / `StringOperatorBenchmark` | Igualdade/hash de `JobKey`/`TriggerKey`; operações de string |

### Metodologia de throughput (macro-benchmark)

`src/Quartz.Benchmark/SchedulerBenchmark.cs` — mede execuções/segundo e **alocações**, com asserção de correção:

```csharp
[MemoryDiagnoser]                                  // relatório de bytes alocados por operação
public class SchedulerBenchmark
{
    [Benchmark(OperationsPerInvoke = 450_000)]     // amortiza medição sobre 450k execuções
    public void Concurrent_15Threads_15Jobs_1TriggersPerJob()
        => RunConcurrent(operationsPerRun: 450_000, threadCount: 15, jobCount: 15,
            disableConcurrentExecution: false, triggersPerJob: 1, maxBatchSize: 16,
            idleWaitTime: TimeSpan.FromTicks(1L),  // NUNCA Zero (ignorado em algumas versões)
            repeatInterval: TimeSpan.FromTicks(1L), repeatCount: 29_999,
            misfireInstruction: MisfireInstruction.IgnoreMisfirePolicy);

    public static void RunConcurrent(int operationsPerRun, int threadCount, /* ... */)
    {
        ConcurrentJob.Initialize(operationsPerRun);
        var scheduler = CreateAndConfigureScheduler<ConcurrentJob>(/* RAMJobStore + DefaultThreadPool */);
        scheduler.Start();
        ConcurrentJob.Wait();                        // ManualResetEvent sinalizado na última execução
        scheduler.Shutdown(true).GetAwaiter().GetResult();
        if (ConcurrentJob.RunCount != operationsPerRun)  // correção: falha se contagem não bater
            throw new Exception($"Expected {operationsPerRun}, got {ConcurrentJob.RunCount}.");
        ConcurrentJob.Reset();
    }
}

// O job "medidor": conta com Interlocked e sinaliza o fim.
[DisallowConcurrentExecution]
public class DisableConcurrentJob : IJob
{
    private static readonly ManualResetEvent Done = new(false);
    private static int _runCount, _operationsPerRun;
    public ValueTask Execute(IJobExecutionContext context)
    {
        if (Interlocked.Increment(ref _runCount) == _operationsPerRun) Done.Set();
        return default;
    }
}
```

Para medir **alocação limpa** (sem o custo de criar o scheduler poluir a medição), usam `[IterationSetup]` para montar o scheduler **fora** da região medida:

```csharp
[IterationSetup(Target = nameof(Concurrent_30Threads_15Jobs_1TriggersPerJob_RepeatCountZero))]
public void IterationSetup_...() => _scheduler = CreateAndConfigureScheduler<ConcurrentJob>(/* ... */);

// "Primary goal is to measure allocations": adquire 15 triggers, adquire 0, espera idleWaitTime.
[Benchmark(OperationsPerInvoke = 15)]
public void Concurrent_30Threads_15Jobs_1TriggersPerJob_RepeatCountZero()
{
    ConcurrentJob.Initialize(15);
    _scheduler!.Start();
    ConcurrentJob.Wait();
    _scheduler.Shutdown(false).GetAwaiter().GetResult();
    ConcurrentJob.Reset();
}
```

Padrões-chave: `[MemoryDiagnoser]` (alocações), `[Benchmark(OperationsPerInvoke = N)]` (amortiza N operações por medição), `[IterationSetup]` (setup fora da medição), matriz de parâmetros (threads × jobs × triggers × concurrent/disable), e **asserção de correção** dentro do benchmark.

---

## O que o Guará deve fazer

Criar `guara/benchmarks/` (já previsto em spec 033/034) com um projeto `Guara.Benchmarks` (BenchmarkDotNet, `net8.0;net10.0` para comparar JIT/AOT), espelhando a divisão micro + macro:

| Benchmark do Guará | Mede | Espelha |
|---|---|---|
| `CronExpressionBenchmarks` | `CronExpression.Parse` + `GetNextOccurrence` (com/sem timezone) | `CronExpressionBenchmark` |
| `MemoryStorageBenchmarks` | `AcquireNextDueAsync` throughput + alocação (concorrência de N leitores) | `RAMJobStoreBenchmark` |
| `PipelineBenchmarks` | `JobPipelineBuilder.Build` + execução de N middlewares; **prova o ganho do pool de `JobContext`** (alocação ~0) | `JobDispatchBenchmark`/`JobExecutionContextImplBenchmark` |
| `SerializationBenchmarks` | `SerializeArgs`/`DeserializeArgs` round-trip (alocação; source-gen x reflection) | — (STJ source-gen) |
| `EndToEndThroughputBenchmarks` | `EnfileirarAsync` → execução de N jobs (jobs/s), Memory storage; concurrent x `[GuaraDesabilitarConcorrencia]` | `SchedulerBenchmark` |
| `JobIdBenchmarks` / `JobStateMachineBenchmarks` | Igualdade de `readonly record struct JobId`; transições | `KeyBenchmark` |

Padrão recomendado (idêntico ao Quartz, adaptado ao `TimeProvider`):
- `[MemoryDiagnoser]` em tudo (alocação é o nosso KPI — ADR-0008/performance).
- `[Benchmark(OperationsPerInvoke = N)]` + job medidor com `Interlocked` + `ManualResetEventSlim`; **assert de correção** (contagem == esperado).
- `[IterationSetup]` para montar host/storage fora da medição.
- `[Params]` para varrer concorrência (MaxConcurrency, nº de filas).
- CI: baseline versionado; regressão de alocação/latência acima do limiar **falha** o pipeline (spec 034), mas rodar como **informativo** no início (runners compartilhados têm ruído de latência — alocação é mais estável que tempo).

---

## Armadilhas e detalhes sutis

- **Nunca `TimeSpan.Zero`** em intervalos de idle/poll no benchmark — o Quartz o ignora em algumas versões; use `TimeSpan.FromTicks(1)`. No Guará, injete um `TimeProvider` de teste para agendamento determinístico, mas para throughput real use o relógio de sistema.
- **`ProduceReferenceAssembly=false`** e **`AnalysisLevel=none`** no csproj de benchmark (BDN gera assemblies; analisadores atrapalham).
- **Setup fora da medição** (`[IterationSetup]`) é essencial para benchmarks de alocação — senão a criação do scheduler/host domina os bytes.
- **Correção junto com a medição**: o benchmark deve falhar se o nº de execuções divergir — um benchmark "rápido" que não executa tudo é inútil.
- **Sempre em Release** e fora do depurador; rodar `--filter` para subconjuntos.
- **Multi-target**: rode em `net8.0` e `net10.0` para quantificar o ganho do runtime moderno e validar o caminho AOT (o Guará se vende como AOT-ready).
- **Warmup**: deixe o BDN gerenciar (não faça warmup manual); mas caminhos com JIT de genéricos fechados (nosso `IEventHandler<TEvent>`) precisam de warmup suficiente.
