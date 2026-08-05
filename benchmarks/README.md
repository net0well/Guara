# Benchmarks

Medições de caminho crítico com [BenchmarkDotNet](https://benchmarkdotnet.org/). Existem para que as afirmações de [docs/performance.md](../docs/performance.md) tenham número, e para que uma regressão apareça como diferença medida em vez de sensação.

## Rodando

Benchmark só mede em **Release**:

```bash
dotnet run --project benchmarks/Guara.Scheduler.Benchmarks -c Release -- --filter "*"
```

Durante o desenvolvimento, `--job short` troca precisão por tempo (3 iterações em vez do padrão):

```bash
dotnet run --project benchmarks/Guara.Serialization.Benchmarks -c Release -- --filter "*" --job short
```

Feche o que estiver disputando CPU antes de medir e desconfie de qualquer número colhido em máquina ocupada — o desvio padrão da saída denuncia.

## O que cada projeto mede

| Projeto | Mede | Por que importa |
|---|---|---|
| `Guara.Serialization.Benchmarks` | `SerializeArgs`/`DeserializeArgs` e o par tipado `Serialize<T>`/`Deserialize<T>` | Pago duas vezes por job: ao enfileirar e ao executar |
| `Guara.Scheduler.Benchmarks` | `GetNextOccurrence` para expressões cron de custo crescente; `EnfileirarAsync`/`AgendarAsync` pela composição real | O cron roda por definição recorrente a cada ciclo; o enfileiramento é a linha que o usuário escreve |
| `Guara.Storage.Memory.Benchmarks` | `CreateAsync`, `AcquireNextDueAsync` e `GetAsync` em três profundidades de fila | O provider in-memory não tem rede nem disco, então o que sobra é o custo do próprio Guará |
| `Guara.Throughput.Harness` | Vazão e latência com worker e dispatcher rodando de verdade, sobre storage real | É o número que se pergunta primeiro: quantos jobs por segundo, e quanto demora até começar |

### O harness de vazão

Não é BenchmarkDotNet — é um console que sobe container, roda o ciclo completo e imprime tabela:

```bash
dotnet run --project benchmarks/Guara.Throughput.Harness -c Release -- \
    --storage postgresql --jobs 10000 --concurrency 1,4,16,64
```

`--storage` aceita `memory`, `postgresql` e `sqlserver`. As fases são separadas de propósito: enfileirar e drenar ao mesmo tempo misturaria os dois custos num número só, e latência medida sob backlog seria apenas a posição do job na fila. O intervalo de busca do dispatcher fica alto (60 s por padrão) para que a latência medida seja a do aviso de fila, não a do polling.

## Resultados de referência

Colhidos em `--job short` num Xeon E5-2670 v3 (2.30 GHz), .NET 10.0.10, Windows 11. **Servem para comparação relativa, não como promessa de desempenho** — a máquina é modesta e a configuração curta.

### Serialização

| Operação | Tempo | Alocado |
|---|---:|---:|
| `SerializeArgs` — 3 escalares | 898 ns | 984 B |
| `SerializeArgs` — 8 argumentos, texto de 4 KB | 4.779 ns | 13.024 B |
| `DeserializeArgs` — 3 escalares | 2.735 ns | 320 B |
| `DeserializeArgs` — 8 argumentos, texto de 4 KB | 8.098 ns | 8.880 B |
| `Serialize<JobDescriptor>` | 381 ns | 136 B |
| `Deserialize<JobDescriptor>` | 841 ns | 304 B |

A alocação acompanha o payload, não a quantidade de tipos na allowlist — que é o que se queria do caminho source-gen.

### Cron e enfileiramento

| Operação | Tempo | Alocado |
|---|---:|---:|
| Cron `* * * * *` | 101 ns | **0 B** |
| Cron `*/15 9-18 * * 1-5` | 240 ns | **0 B** |
| Cron `0 3 * * *` | 467 ns | **0 B** |
| Cron `0 3 * * *` com fuso (conversão + DST) | 789 ns | **0 B** |
| Cron `0 0 29 2 *` (atravessa anos até o bissexto) | 1.571 ns | **0 B** |
| `EnfileirarAsync` | 1.219 ns | 280 B |
| `AgendarAsync` (com atraso) | 1.532 ns | 320 B |

O parser cron próprio calcula o próximo disparo **sem alocar**, inclusive no pior caso — o `29 de fevereiro`, que precisa atravessar anos até achar um bissexto, custa 1,5 µs e nenhum byte.

### Storage in-memory

| Operação | Profundidade | Tempo | Alocado |
|---|---:|---:|---:|
| `CreateAsync` | 1 | 794 ns | 256 B |
| `CreateAsync` | 1.000 | 772 ns | 256 B |
| `CreateAsync` | 50.000 | 786 ns | 256 B |
| `GetAsync` | qualquer | ~40 ns | 0 B |
| `AcquireNextDueAsync` | 1 | 41 ns | 0 B |
| `AcquireNextDueAsync` | 1.000 | 5.458 ns | 0 B |
| `AcquireNextDueAsync` | 50.000 | 392.957 ns | 152 B |

### Vazão e latência ponta a ponta

| Storage | Concorrência | Enfileiramento | Execução | Latência p50 | p95 | Alocado/job |
|---|---:|---:|---:|---:|---:|---:|
| In-memory (20.000 jobs) | 1 | 464.268/s | 3.397/s | 0,1 ms | 0,2 ms | 2.291 B |
| | 4 | 783.417/s | 3.249/s | 0,1 ms | 0,1 ms | 2.162 B |
| | 16 | 650.487/s | 3.119/s | 0,1 ms | 0,1 ms | 2.145 B |
| | 64 | 784.732/s | 3.597/s | 0,1 ms | 0,1 ms | 2.387 B |
| PostgreSQL (10.000 jobs) | 1 | 541/s | 141/s | 7,4 ms | 9,0 ms | 21.509 B |
| | 4 | 549/s | 149/s | 6,0 ms | 7,3 ms | 21.813 B |
| | 16 | 563/s | 136/s | 5,8 ms | 7,6 ms | 22.962 B |
| | 64 | 569/s | 151/s | 6,1 ms | 7,5 ms | 27.581 B |

A latência é de fire-and-forget com a fila vazia, medida do enfileiramento ao início da execução, com o intervalo de busca em 60 s — ou seja, é o aviso de fila trabalhando. Sem ele, cada um desses números seria dezenas de segundos.

### Progressão no PostgreSQL (10.000 jobs)

Vazão de execução, jobs por segundo:

| Etapa | 1 worker | 4 | 16 | 64 |
|---|---:|---:|---:|---:|
| Original | 141 | 149 | 136 | 151 |
| Elegibilidade indexada ([ADR-0015](../docs/adr/0015-elegibilidade-como-instante-indexavel.md)) | 190 | 523 | 502 | 494 |
| **+ preparação automática de comandos** | **207** | **671** | **650** | **639** |

**4,5× acumulado** com 4 workers, e a vazão voltou a responder à concorrência — de 1 para 4 workers sobe 3,2×, contra 1,06× para 64× no começo.

As duas mudanças atacaram gargalos diferentes: a primeira tirou do caminho um `Sort` proporcional à profundidade da fila; a segunda parou de fazer o servidor replanejar as mesmas consultas a cada chamada, o que o `EXPLAIN` mostrava custar 1,6 a 2,5 ms por aquisição.

## Achados

**A vazão não escala com a concorrência do worker — o teto está na aquisição.** Multiplicar `MaxConcurrency` por 64 rendeu 1,06× no in-memory e 1,07× no PostgreSQL. Plano nos dois, e é isso que isola a causa: a escrita de estado do lado da execução roda em paralelo entre os workers, então, se ela dominasse, a concorrência teria escalado. Não moveu. O que sobra é a parte serial.

O `GuaraDispatcher` adquire **um job por vez**, num laço único: `AcquireNextDueAsync` volta no máximo um registro, e cada chamada é uma ida completa ao banco. No PostgreSQL isso dá ~7 ms por job e trava em ~140 jobs/s, com 64 workers ociosos esperando um alimentador de fila única. O esquema não entra na conta — `EnsureAsync` é memoizado e vira no-op depois da primeira chamada.

O desenho desperdiça justamente a propriedade pela qual o SQL foi escolhido: `FOR UPDATE SKIP LOCKED` e `READPAST` existem para que N consumidores adquiram em paralelo sem contenção.

Duas saídas, ambas com impacto direto no número: **aquisição em lote** (devolver N jobs por ida — `LIMIT N` com `RETURNING`, `TOP (N)` com `OUTPUT`) e **aquisição concorrente** (mais de um laço buscando). A primeira muda `IJobStorage`, então precisa entrar antes do congelamento da API pública.

**A alocação por job merece investigação.** 2,2 KB no in-memory e ~21 KB no PostgreSQL, por job executado. O driver e o JSON explicam parte da diferença, mas o número absoluto não está medido por etapa — é o benchmark de pipeline que ainda falta.

**`MemoryStorage.AcquireNextDueAsync` é O(n) na profundidade da fila.** De 41 ns com um job para 393 µs com cinquenta mil — cerca de 9.600× para 50.000× mais jobs, ou seja, varredura linear do dicionário atrás do mais antigo elegível. Com um backlog desse tamanho, um worker fica limitado a algo perto de 2.500 aquisições por segundo só na varredura.

Não é regressão nem bug: o provider in-memory é declaradamente para **desenvolvimento, testes e demos**, e os providers persistentes resolvem isso com índice no banco. Mas é característica real, que aparece em teste de carga feito sobre ele e que a documentação não registrava. Está anotada agora aqui e no próprio `MemoryStorage`. Corrigir exigiria manter um índice ordenado por fila — trabalho que só se justifica se alguém precisar do provider in-memory sob backlog.

**`CreateAsync` é constante na profundidade**, como esperado de inserção em dicionário.

## O que ainda não é medido

Registrado para não passar por cobertura completa:

- **Pipeline de execução e pool de `JobContext`** — o custo que o framework acrescenta por job executado. É o número mais pedido e ainda não existe.
- **Providers persistentes** — PostgreSQL, SQL Server, MySQL e MongoDB. Exigem container, o que não combina com o modelo de medição do BenchmarkDotNet; precisa de harness próprio.
- **SQL Server e MySQL no harness** — o harness já os aceita, mas os números publicados aqui são de in-memory e PostgreSQL.
- **Comparação com Hangfire e Quartz** — o harness foi desenhado para receber um segundo cenário sem retrabalho, mas nenhum foi escrito.
- **Comparação de baseline no CI.** Hoje os benchmarks rodam quando alguém os roda. Não há baseline versionado nem gate de regressão.
