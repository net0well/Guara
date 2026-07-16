# ADR-0002 — Comunicação por Eventos entre Componentes

- **Status:** Aceito
- **Data:** 2026-07-16

## Contexto

Se o `Dispatcher` chamar `Worker.RunAsync(...)` diretamente, ele passa a conhecer a implementação do Worker, e o acoplamento reaparece. Precisamos que o fluxo `Scheduler → Dispatcher → Worker → Executor` funcione sem que um componente referencie o outro.

## Decisão

Componentes se comunicam por **eventos** (notificação assíncrona) e por **contratos** (interfaces em `Guara.Abstractions`) — **nunca** por referência à classe concreta de outro componente.

Fluxo canônico:

```
JobCreated → JobScheduled → WorkerRequested → ExecutorStarted → JobCompleted
```

Eventos são nomeados no passado, definidos em `Guara.Abstractions`, e trafegam por um event bus interno sobre `Channel<T>` ([ADR-0004](0004-channel-para-filas-internas.md)).

## Consequências

**Ganhos:** substituição/observabilidade fácil (qualquer componente pode assinar um evento — Diagnostics, Notifications); componentes testáveis isoladamente; caminho natural para o modo distribuído (`Guara.Cluster`).

**Custos:** rastrear um fluxo exige seguir eventos, não a pilha de chamadas; ordenação e entrega precisam de garantias explícitas; risto de "eventos órfãos" sem consumidor — mitigado por testes de contrato.

Detalhado em [../execution-flows.md](../execution-flows.md).
