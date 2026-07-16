# Princípios de Performance

Performance é requisito arquitetural, não otimização posterior. **Cada componente deve respeitar** as regras abaixo. Componentes de caminho crítico têm projeto em `benchmarks/` (BenchmarkDotNet) com baseline versionado.

## Regras

| Princípio | Regra prática | Por quê |
|---|---|---|
| **Zero reflection em runtime** | Descoberta/registro via Source Generators; nada de `Activator.CreateInstance`/`Type.GetType` no hot path | Reflection aloca, é lenta e quebra AOT/Trimming. [ADR-0005](adr/0005-source-generators-para-registro.md) |
| **`Channel<T>` para filas internas** | Filas de eventos/jobs usam `System.Threading.Channels` | Backpressure, sem locks manuais, alto throughput. [ADR-0004](adr/0004-channel-para-filas-internas.md) |
| **`ValueTask` em APIs críticas** | Aquisição de job, publicação de evento, leitura de storage devolvem `ValueTask` | Evita alocação de `Task` no caminho síncrono-frequente |
| **`Span<T>` / `Memory<T>`** | Parsing (cron, payload) e buffers usam spans | Zero cópia, zero alocação intermediária |
| **Object Pool** | Contexto de job, buffers e `StringBuilder` de curta duração saem de pool | Reduz pressão no GC em loop quente |
| **Baixa alocação** | Preferir `struct`/`readonly struct` para valores pequenos; evitar closures em hot path | Menos Gen0/Gen1, menos pausas de GC |
| **Async completo + `CancellationToken`** | Nada de `.Result`/`.Wait()`/`Thread.Sleep`; token propagado ponta a ponta | Não bloquear threads do pool |
| **Thread safety por padrão** | Estado compartilhado imutável ou protegido; sem singleton estático mutável | Correção sob concorrência sem locks grosseiros |
| **Native AOT + Trimming** | Sem reflection dinâmica, sem `dynamic`, atributos de trimming quando necessário | Startup rápido, binário menor. [ADR-0008](adr/0008-native-aot-e-trimming.md) |

## Padrões de código

```csharp
// ValueTask no hot path
public ValueTask<JobRecord?> AcquireNextAsync(CancellationToken ct);

// Channel para fila interna (backpressure via BoundedChannelOptions)
private readonly Channel<JobId> _queue =
    Channel.CreateBounded<JobId>(new BoundedChannelOptions(capacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false
    });

// Object Pool para contexto de curta duração
private static readonly ObjectPool<JobContext> ContextPool =
    ObjectPool.Create<JobContext>();

// readonly struct para identificador (sem alocação, comparável)
public readonly record struct JobId(long Value);
```

## O que evitar (resumo)

- `Task` onde cabe `ValueTask` no caminho quente.
- Reflection para achar handlers/jobs → use Source Generators.
- LINQ com muitas alocações em loop quente → laços diretos ou `Span`.
- Boxing de `struct` (ex.: guardar `JobId` como `object`).
- Locks grosseiros em torno de coleções → prefira `Channel<T>` ou coleções concorrentes.
- Alocar buffers por iteração → `ArrayPool<T>`/`ObjectPool<T>`.

Ver também [anti-patterns.md](anti-patterns.md) (itens 9, 10, 14, 15).

## Verificação

- Todo componente crítico tem benchmark em `benchmarks/Guara.{Componente}.Benchmarks`.
- Regressões de alocação/latência acima do limiar do baseline **falham** o CI.
- Builds AOT (`PublishAot=true`) fazem parte da matriz de CI para os pacotes runtime.
