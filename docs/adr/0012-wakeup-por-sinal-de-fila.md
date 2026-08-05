# ADR-0012 — Wakeup por Sinal de Fila

- **Status:** Aceito
- **Data:** 2026-08-05

## Contexto

O `Guara.Dispatcher` descobre trabalho por polling: busca jobs elegíveis e, quando não há nenhum, dorme `PollingInterval` (5s por padrão). Isso custa nos dois extremos.

Na latência: um job enfileirado logo depois de um ciclo vazio espera até o próximo — em média metade do intervalo, no pior caso o intervalo inteiro. Baixar o intervalo melhora a latência e piora o custo.

No custo ocioso: cada ciclo é um `UPDATE ... RETURNING` por fila configurada, em cada nó. Vinte nós, três filas, intervalo de 1s dão 60 escritas por segundo contra o banco só para descobrir que não há nada a fazer.

Os dois lados do trade-off vêm do mesmo lugar: o dispatcher não tem como saber que chegou trabalho, então pergunta. O que falta é o caminho inverso — quem enfileira avisar quem busca.

`StorageCapabilities.SupportsServerSideTimers` já reservava o conceito, mas nenhum contrato o exercia.

## Decisão

Um contrato de aviso, `IQueueSignal` (em `Guara.Abstractions`), com dois lados:

```csharp
ValueTask SignalAsync(string queue, CancellationToken ct);
ValueTask<bool> WaitAsync(IReadOnlyList<string> queues, TimeSpan timeout, CancellationToken ct);
```

Quem torna um job elegível **agora** avisa a fila; o dispatcher, ao encontrar a fila vazia, aguarda o aviso em vez de dormir um tempo fixo. `PollingInterval` deixa de ser o ritmo e passa a ser o **teto** da espera.

Quatro propriedades sustentam o desenho:

1. **O sinal é best-effort.** Perder um aviso atrasa a busca até o próximo ciclo; nunca perde um job. O polling continua sendo o piso, e é isso que torna seguro descartar sinais sob pressão, tolerar falha de transporte e não exigir transação entre persistir o job e avisar.

2. **O aviso é retido.** Um sinal emitido enquanto ninguém aguarda satisfaz a próxima espera. Sem isso, um job que entra entre a última busca e o início da espera ficaria parado até o timeout — a corrida mais provável que existe aqui, já que as duas coisas acontecem em sequência imediata.

3. **Só sinaliza o que já é elegível.** Retentativa, reagendamento e continuação pendente têm data futura: avisar acordaria o dispatcher para não achar nada. Quem cobre o futuro é o ciclo periódico.

4. **A implementação padrão é em processo.** `InProcessQueueSignal` (em `Guara.Core`, registrado por `AddGuara()`) resolve o caso de nó único sem qualquer infraestrutura. Transportes externos substituem o registro e ganham o alcance entre nós — é o ponto de extensão em que o `Guara.Redis` se liga por pub/sub.

## Consequências

**Ganhos:** latência de despacho passa de "metade do intervalo" para o tempo de uma notificação, sem baixar o intervalo; o custo ocioso contra o banco cai porque o intervalo pode subir; o dispatcher fica com um único caminho de código, com o transporte trocável por DI; nó único melhora sem depender de infraestrutura nova.

**Custos:** mais um contrato na superfície pública, com semântica que precisa ser respeitada por quem implementa (retenção do aviso e tolerância a perda); quem enfileira passa a fazer uma chamada a mais, que num transporte remoto é I/O — por isso a falha é isolada e nunca propaga para o enfileiramento.

Relaciona-se a [ADR-0002](0002-comunicacao-por-eventos.md) (comunicação por contrato), [ADR-0004](0004-channel-para-filas-internas.md) (filas internas) e [ADR-0006](0006-uma-extensao-addguara-por-pacote.md) (registro por pacote).
