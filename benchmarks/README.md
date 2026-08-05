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

## Achados

**`MemoryStorage.AcquireNextDueAsync` é O(n) na profundidade da fila.** De 41 ns com um job para 393 µs com cinquenta mil — cerca de 9.600× para 50.000× mais jobs, ou seja, varredura linear do dicionário atrás do mais antigo elegível. Com um backlog desse tamanho, um worker fica limitado a algo perto de 2.500 aquisições por segundo só na varredura.

Não é regressão nem bug: o provider in-memory é declaradamente para **desenvolvimento, testes e demos**, e os providers persistentes resolvem isso com índice no banco. Mas é característica real, que aparece em teste de carga feito sobre ele e que a documentação não registrava. Está anotada agora aqui e no próprio `MemoryStorage`. Corrigir exigiria manter um índice ordenado por fila — trabalho que só se justifica se alguém precisar do provider in-memory sob backlog.

**`CreateAsync` é constante na profundidade**, como esperado de inserção em dicionário.

## O que ainda não é medido

Registrado para não passar por cobertura completa:

- **Pipeline de execução e pool de `JobContext`** — o custo que o framework acrescenta por job executado. É o número mais pedido e ainda não existe.
- **Providers persistentes** — PostgreSQL, SQL Server, MySQL e MongoDB. Exigem container, o que não combina com o modelo de medição do BenchmarkDotNet; precisa de harness próprio.
- **Vazão ponta a ponta** — jobs por segundo com worker e dispatcher rodando de verdade.
- **Comparação de baseline no CI.** Hoje os benchmarks rodam quando alguém os roda. Não há baseline versionado nem gate de regressão.
