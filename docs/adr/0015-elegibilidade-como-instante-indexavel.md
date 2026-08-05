# ADR-0015 — Elegibilidade como Instante Indexável

- **Status:** Aceito
- **Data:** 2026-08-05

## Contexto

O harness de vazão mostrou que a execução não escala com a concorrência do worker: multiplicar `MaxConcurrency` por 64 rendeu 1,06× no storage in-memory e 1,07× no PostgreSQL. Plano nos dois, o que isola a causa — a escrita de estado do lado da execução roda em paralelo entre os workers, então, se ela dominasse, a concorrência teria escalado.

A sonda de diagnóstico apontou o culpado no plano de consulta da aquisição:

```
Limit                       (actual time=11.329..11.333 rows=1)
  -> Sort                   (actual time=11.328..11.330 rows=1)
       Sort Key: created_at
       -> Bitmap Heap Scan  (actual time=2.960..7.413 rows=9500)
            -> BitmapOr
                 -> Bitmap Index Scan (queue, state = Enqueued)
                 -> Bitmap Index Scan (queue, state IN (Scheduled, Retrying))
                 -> Bitmap Index Scan (queue, state = Processing)
```

**Para adquirir um job, o banco lê 9.500 linhas e ordena todas — e descarta 9.499.** O custo cresce linearmente com a profundidade da fila: ~0,9 ms com 500 elegíveis, ~11,7 ms com 9.500.

A causa é a forma do predicado. Elegibilidade hoje é uma **disjunção sobre estados**, cada ramo com sua própria coluna de tempo:

- `Enqueued` — elegível já
- `Scheduled` ou `Retrying` — elegível quando `scheduled_for <= agora`
- `Processing` — elegível quando `lease_until < agora`, isto é, posse abandonada

Com o índice em `(queue, state, created_at)`, o banco consegue seguir cada ramo, mas não consegue percorrer **um** intervalo já ordenado por `created_at` — precisa unir os três e então ordenar o resultado inteiro para descobrir o primeiro. É por isso que `ORDER BY created_at LIMIT 1` vira "materializa tudo que é elegível, ordena, pega um".

Nenhuma quantidade de aquisição em lote conserta isso: cada lote ainda ordenaria a fila inteira. Amortizaria o custo entre N jobs, mas deixaria de pé um custo que cresce com o backlog — exatamente quando mais se precisa de vazão.

## Decisão

Elegibilidade deixa de ser derivada de estado no momento da consulta e passa a ser **um instante materializado na linha**: a coluna `eligible_at`.

| Estado | `eligible_at` | Por quê |
|---|---|---|
| `Enqueued` | `created_at` | Elegível já; preserva a ordem de criação entre os enfileirados |
| `Scheduled`, `Retrying` | `scheduled_for` | Elegível quando vence |
| `Processing` | `lease_until` | Elegível quando a posse expira e o job é abandonado |
| `Succeeded`, `Failed` | `NULL` | Nunca elegível |
| `Scheduled` sem `scheduled_for` | `NULL` | Continuação aguardando o pai; nunca elegível até o gatilho |

A aquisição vira uma varredura ordenada que **para na primeira linha**:

```sql
SELECT id FROM jobs
WHERE queue = @queue AND eligible_at <= @now
ORDER BY eligible_at
LIMIT 1
FOR UPDATE SKIP LOCKED
```

com índice em `(queue, eligible_at)`. `eligible_at <= @now` já exclui os nulos, então estado terminal e continuação pendente saem da varredura sem cláusula extra.

**A aquisição continua sendo um único comando atômico.** Essa é a razão de escolher a coluna em vez de reescrever o predicado como `UNION ALL` de três ramos: o PostgreSQL proíbe `FOR UPDATE` com `UNION`, então aquele caminho quebraria a aquisição em "busca o id, depois atualiza se ainda elegível", com laço de repetição sob contenção. A garantia de entrega única do Guará se apoia no comando único — não vale trocá-la por desempenho.

### O que muda na ordem de execução

A ordenação passa de `created_at` para `eligible_at`. Entre jobs `Enqueued` os dois são iguais, então o caso comum não muda. As diferenças aparecem no resto:

- Um job agendado que venceu entra pela hora em que **ficou elegível**, não pela hora em que foi criado.
- Um job cuja posse expirou volta pela hora da expiração. Antes ele voltava pela data de criação, ou seja, ia para a frente da fila — o que fazia um job que derruba o worker repetidamente monopolizar a cabeça da fila a cada retomada.

`docs/semantics.md` passa a descrever "início ~FIFO por elegibilidade". A garantia continua sendo *best-effort*, como sempre foi: com N workers concorrentes nunca houve ordem de conclusão garantida.

### Migração

A DDL de cada provider é idempotente e ganha a coluna, o índice novo e o backfill das linhas existentes. O índice antigo, em `(queue, state, created_at)`, é removido — deixá-lo custaria escrita a cada transição de estado sem servir a nenhuma consulta.

Todos os providers passam a manter `eligible_at` em toda transição de estado, e o kit de conformidade ganha caso que exige a mesma ordem dos seis — divergir aqui seria dois storages com semânticas diferentes sob o mesmo contrato.

## Consequências

**Ganhos:** a aquisição deixa de ser proporcional à profundidade da fila; o custo por job passa a ser um seek em índice, que é o que sustenta qualquer meta de vazão sob backlog; a atomicidade de um comando só permanece intacta; e a ordem de retomada de job abandonado deixa de furar a fila.

**Custos:** migração de esquema em quatro providers, com backfill; toda transição de estado passa a escrever mais uma coluna, o que é custo de escrita em troca de custo de leitura — a proporção favorece a leitura, já que a aquisição roda muito mais vezes que a transição; e a ordem documentada muda, ainda que na direção que faz mais sentido.

**Exposição a relógio dessincronizado.** Antes, um job `Enqueued` era elegível pelo estado, independente de `created_at`. Agora ele fica elegível a partir do próprio carimbo de criação, então um job criado por um nó adiantado fica invisível para os demais pela diferença entre os relógios. A alternativa — manter `state = Enqueued` como ramo à parte no predicado — devolveria ao planejador a disjunção que causou o `Sort`, ou seja, devolveria o problema que este ADR existe para resolver. Fica registrado em [semantics.md](../semantics.md): quem roda vários nós já precisa de relógios sincronizados para lease e posse funcionarem, e esta passa a ser mais uma razão.

**Medição.** No PostgreSQL, com 10.000 jobs: 149 → 523 jobs/s com 4 workers (3,5×), e a vazão voltou a responder à concorrência — de 1 para 4 workers subiu 2,75×, contra 1,06× para 64× antes. O teto seguinte deixou de ser a consulta e passou a ser o número de idas ao banco.

**Não resolvido aqui:** o planejamento da consulta custa 1,6 a 2,5 ms por chamada porque o comando não é preparado, e a aquisição em lote continua em aberto. Os dois entram depois de re-medir, para que o ganho desta mudança apareça isolado.

Relaciona-se a [ADR-0003](0003-abstracao-de-storage-por-provider.md) (contrato de provider) e [ADR-0012](0012-wakeup-por-sinal-de-fila.md) (o aviso que trouxe a latência para submilissegundo e deixou a vazão como o gargalo visível).
