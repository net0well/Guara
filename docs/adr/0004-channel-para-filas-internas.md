# ADR-0004 — `Channel<T>` para Filas Internas

- **Status:** Aceito
- **Data:** 2026-07-16

## Contexto

O event bus interno e as filas entre motores (dispatch → execução) precisam de um mecanismo produtor/consumidor de alto throughput, com backpressure e sem locks manuais. Opções: `BlockingCollection<T>` (bloqueia threads), fila própria com locks (propensa a erro), `Channel<T>` do `System.Threading.Channels`.

## Decisão

Filas internas usam **`System.Threading.Channels`**. Filas limitadas usam `BoundedChannelOptions` com `FullMode = Wait` para backpressure.

```csharp
Channel.CreateBounded<JobId>(new BoundedChannelOptions(capacity)
{
    FullMode = BoundedChannelFullMode.Wait,
    SingleReader = false,
    SingleWriter = false
});
```

## Consequências

**Ganhos:** async de ponta a ponta (sem bloquear threads do pool); backpressure nativo; sem sincronização manual; ótima performance e baixa alocação; integra com `CancellationToken`.

**Custos:** capacidade e política de `FullMode` viram decisões de tuning por fila; consumidores precisam tratar `ChannelClosedException` no shutdown.

Relacionado a [../performance.md](../performance.md) e [ADR-0002](0002-comunicacao-por-eventos.md).
