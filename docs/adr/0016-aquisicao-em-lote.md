# ADR-0016 — Aquisição em Lote

- **Status:** Aceito
- **Data:** 2026-08-05

## Contexto

Depois de [ADR-0015](0015-elegibilidade-como-instante-indexavel.md), a consulta de aquisição deixou de custar proporcional à profundidade da fila. A sonda de diagnóstico mediu o que sobrou, em duas profundidades por provider:

| Provider | Piso (`SELECT 1`) | Aquisição | ÷ piso | Cresce com a fila? |
|---|---:|---:|---:|:-:|
| SQL Server | 2,1 ms | 5,7 ms | 2,8× | não |
| MySQL | 1,5 ms | 12,0 ms | 8,4× | não |

Nenhum custo cresce com a profundidade, e o plano do MySQL devolve uma linha por varredura de índice coberto em 0,1 ms. **A consulta não é mais o gargalo em provider nenhum.** O que sobra é protocolo: ida-e-volta ao banco.

E o dispatcher gasta uma ida por job. `AcquireNextDueAsync` devolve no máximo um registro, num laço sequencial, então a vazão de execução fica presa no custo dessa ida — independente de quantos workers estejam livres. Foi o que a medição ponta a ponta mostrou desde o começo: 64× mais concorrência rendeu 1,06× de vazão.

No MySQL o efeito é o maior, porque a aquisição carrega `BEGIN`, `SELECT ... FOR UPDATE SKIP LOCKED`, `UPDATE` e `COMMIT` — a transação explícita existe porque o MySQL não tem `RETURNING`, e o commit ainda paga flush durável do InnoDB.

## Decisão

A aquisição passa a devolver **até N jobs por chamada**. `IJobStorage` troca a operação de um job pela de lote:

```csharp
ValueTask<IReadOnlyList<JobRecord>> AcquireNextDueAsync(
    string queue, int max, TimeSpan lease, DateTimeOffset now, CancellationToken ct);
```

Uma operação, não duas. Manter também a versão de um job criaria dois caminhos para a mesma coisa e um membro que a produção nunca chamaria — o problema que [ADR-0014](0014-enfileiramento-transacional.md) acabou de remover do contrato. Quem quer um job pede `max: 1`.

### O tamanho vem da capacidade ociosa do worker

O dispatcher pede quantos jobs couberem nos slots livres **no momento da chamada**, através de um contrato novo em `Guara.Abstractions`:

```csharp
public interface IWorkerCapacity
{
    int Available { get; }
}
```

Contrato próprio, e não um membro a mais em `IWorker`, para que o dispatcher não ganhe acesso a `StartAsync`/`StopAsync` — ele não tem por que poder ligar e desligar o worker.

Três consequências vêm de graça dessa escolha:

- **O backpressure continua igual.** O dispatcher nunca busca além do que consegue entregar, que é a propriedade que o canal limitado já garantia.
- **Carga leve não paga latência.** Com um job na fila e slots livres, pede-se pouco e entrega-se na hora; o lote não introduz espera para encher.
- **N é pequeno por construção.** O teto é a capacidade do worker, não um número configurado que pode não ter relação com ela.

### Posse: N pendentes na queda de um nó

Um nó que cai com N jobs adquiridos deixa N posses para expirar, em vez de uma. Isso é **aceito e documentado**, não mitigado.

O lease já é o mecanismo que cobre queda de nó: os jobs voltam a ser elegíveis quando a posse vence. O lote muda a quantidade, não a natureza. E como o tamanho vem da capacidade ociosa, N é pequeno — um nó com 8 slots livres arrisca 8 jobs, não a fila inteira.

A alternativa de encurtar o lease no lote foi descartada: quem executa por último no lote pode perder a posse antes de começar, e a renovação só começa quando o job entra em execução. Trocaria um problema raro por um recorrente.

### Todos os providers implementam

`Guara.Storage.Memory` e `Guara.Storage.Mongo` podem implementar em laço sobre a operação de um job: sem ganho, mas sem divergir do contrato. Deixar o lote opcional por capacidade criaria mais um eixo em que providers se comportam diferente — e o caso de conformidade de ordem, escrito em [ADR-0015](0015-elegibilidade-como-instante-indexavel.md), acabou de mostrar que divergência silenciosa entre providers só aparece em produção.

## Consequências

**Ganhos:** o custo fixo de ida-e-volta passa a ser dividido por N, que é a camada onde a medição mostrou o gargalo em todos os providers relacionais; o ganho é maior justamente onde o custo por aquisição é maior, e no MySQL uma transação passa a cobrir N jobs em vez de um; e a vazão volta a responder à concorrência do worker, que é o que se esperava desde o começo.

**Custos:** mudança em `IJobStorage`, que é contrato público — precisa entrar antes do congelamento da API; mais um contrato em `Guara.Abstractions`; e a janela de posse pendente numa queda passa a valer para N jobs.

**Não resolvido aqui:** o piso de 2,1 ms do SQL Server continua o que é — característica do ambiente de medição, não do Guará —, e a alocação por job segue sem medição por etapa.
