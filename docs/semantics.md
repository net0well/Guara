# Semântica de Execução — Garantias e Comportamentos

Documento **canônico** das garantias do Guará. Toda funcionalidade se comporta como descrito aqui; qualquer divergência é bug. Decisões registradas em 2026-07-17 com o autor do projeto.

## Garantia de entrega: at-least-once

O Guará garante **pelo menos uma execução** por job (*at-least-once*). Um job **pode executar mais de uma vez** no cenário de falha: o worker morre depois de fazer o trabalho mas antes de persistir o estado final → o lease expira → outro nó reprocessa.

| Se o seu job... | Faça |
|---|---|
| É idempotente (rodar 2x não faz mal) | Nada — é o caso ideal |
| Tem efeito colateral irreversível (cobrança, e-mail) | `[GuaraRetentativas(0)]` + idempotência na ponta (chave de dedupe no destino) |
| Não pode rodar em paralelo consigo mesmo | `[GuaraDesabilitarConcorrencia]` (mutex por chave, entre nós) |
| Precisa de dedupe no enfileiramento | `IdempotencyKey` via `Guara.Distributed` (opt-in, spec 026) |

**Exatamente-uma-vez não existe em sistemas distribuídos** — o Guará não finge oferecer; oferece as ferramentas acima.

## Deduplicação de enfileiramento

**Nenhuma por padrão**: cada `EnfileirarAsync`/`AgendarAsync` cria um job novo com id novo — chamar 3x = 3 jobs. Dedupe é opt-in (`IdempotencyKey`, spec 026). Recorrentes são a exceção: `AdicionarOuAtualizarRecorrenteAsync` é **upsert** por `ComId`.

## Ordem de execução

- **Início ~FIFO por fila** (por **elegibilidade**), *best-effort* — com N workers concorrentes, a ordem de **conclusão** não é garantida. Ver [ADR-0015](adr/0015-elegibilidade-como-instante-indexavel.md).
- Elegibilidade é o instante em que o job passa a poder ser adquirido: `CreatedAt` para enfileirado, `ScheduledFor` para agendado e retentativa, `LeaseUntil` para posse abandonada. Entre jobs enfileirados isso equivale à ordem de criação; um agendado que venceu entra pela hora em que ficou elegível, e um job abandonado volta pela hora em que a posse expirou — sem furar a fila.
- Consequência de datar no futuro: um job **enfileirado** com `CreatedAt` adiante do relógio do nó que busca só fica elegível quando aquele instante chega. Com relógios dessincronizados entre nós, isso atrasa o job pela diferença.
- **Nenhuma garantia de ordem global** entre filas ou entre nós. Precisa de ordem estrita? Encadeie com `ContinuarComAsync`.
- Retentativas reentram na fila — um job que falhou pode concluir depois de jobs criados após ele.

## Precisão temporal

Delayed/recorrentes disparam **na primeira varredura elegível após vencer** — a precisão depende do `PollingInterval` (default 5s) ou do push do provider (`LISTEN/NOTIFY` etc.). O Guará **não é um sistema de tempo real**: `AgendarAsync(x, 10s)` significa "não antes de 10s; tipicamente até PollingInterval depois".

## Retentativas

- Default: **3 retentativas** com back-off exponencial (`2^tentativa` segundos); por job: `[GuaraRetentativas(n)]`; `0` = nunca retenta.
- Esgotou → `Failed` com o motivo da **última** falha.
- **Retentativa persistente** *(implementada 2026-07-18)*: falha grava `Retrying` + reagendamento com back-off e `Attempt` incrementado **no storage** — sobrevive a restart do nó, a reexecução é adquirida como qualquer job vencido e o dashboard mostra a contagem real. O evento `JobRetryScheduled` sinaliza cada reagendamento; `JobFailed` só dispara na falha definitiva.
- Retentativa **em processo** (sem tocar o storage) continua disponível como middleware opcional (`RetryMiddleware`) para oscilações rápidas dentro de uma mesma tentativa.
- Cancelamento (shutdown/posse perdida) **não conta como tentativa**.

## Cancelamento, tempo limite e efeitos colaterais

- Todo cancelamento é **cooperativo** (o job deve honrar o `CancellationToken`). O Guará nunca aborta thread.
- **Efeito ocorrido nunca é revertido**: o estado final é persistido com token **não-cancelável**.
- **Shutdown/posse perdida no meio da execução** → estado fica intocado (`Processing`); o lease expira e o job reprocessa. Cancelamento **não** é falha.
- **`[GuaraTempoLimite(s)]`**: ao exceder, o token é cancelado.
  - Job honra o token (lança `OperationCanceledException`) → **`Failed`** com motivo "tempo limite".
  - Job **ignora** o token e completa → **`Succeeded` + aviso no log** ("excedeu o tempo limite") — o estado reflete a realidade; o efeito já aconteceu. *(Decisão 2026-07-17.)*
  - Job que ignora o token ocupa o slot até terminar (documentado; use jobs bem-comportados).

## Recorrentes

| Situação | Comportamento |
|---|---|
| Ocorrência anterior ainda em execução quando vence a próxima | **Sobrepõem por padrão** (modelo Quartz/Hangfire). Para não sobrepor: `[GuaraPularSeAnteriorEmExecucao]` (pula e registra) ou `[GuaraDesabilitarConcorrencia]` (re-enfileira). *(Decisão 2026-07-17.)* |
| **Misfire** (host desligado na hora; N ocorrências perdidas) | **Roda UMA ocorrência de compensação** ao religar e recalcula a próxima normal — sem backfill, sem pular. *(Decisão 2026-07-17.)* |
| Pausado → retomado (dashboard) | **Sem backfill**: nada do período pausado roda; próxima ocorrência é a próxima válida após retomar |
| Agenda editada (código ou dashboard) | `NextRun` recalculado **a partir de agora** com a nova agenda |
| `TerminaEm` atingido | Inativo/expirado (visível no dashboard), não excluído |
| Data excluída por calendário | Ocorrência **pulada** para a próxima válida; calendário editado (código **ou** dashboard) recalcula todos os recorrentes que o usam |
| `EntreHorarios(início, fim)` | Governa **o disparo de ocorrências**, nunca interrompe execução em andamento; fora da janela → próximo início; `início > fim` cruza a meia-noite; avaliada no fuso do `NoFusoHorario` |
| Excluir definição | `ExcluirRecorrenteAsync(id)`; ocorrências já enfileiradas/rodando **não** são afetadas. Excluir uma *ocorrência* não afeta a definição |

## Continuations

- Disparo **no estado final** do pai (`OnSucceeded` default; `OnAnyFinishedState` opcional). `Retrying` não é final — o filho espera.
- Pai falhou com gatilho `OnSucceeded` → filho **descartado e registrado** (visível), nunca disparado.
- Pai **excluído** (`ExcluirAsync`) → continuações pendentes **descartadas e registradas**.
- Disparo **idempotente** entre nós (cada filho enfileira exatamente uma vez); registrar continuação em pai já finalizado avalia o gatilho imediatamente.

## Exclusão

- `ExcluirAsync(jobId)` → `false` se inexistente **ou em execução** (`Processing`) — nunca "puxa o tapete" de um job rodando; cancele/aguarde antes.
- Calendário em uso não pode ser excluído (erro com a lista de recorrentes).

## Filas

- **Prioridade estrita** pela ordem da lista (`["alta", "default"]`): o dispatcher drena `alta` antes de olhar `default`. **Starvation é possível por design** — se `alta` nunca esvazia, `default` espera; dimensione filas/workers de acordo (modelo Hangfire). Rodízio ponderado poderá vir como opção extend-only. *(Decisão 2026-07-17.)*
- Backpressure: o dispatcher nunca adquire além da capacidade dos workers. O lote de aquisição é dimensionado pelas vagas livres no momento da busca ([ADR-0016](adr/0016-aquisicao-em-lote.md)), e o canal limitado continua sendo a garantia.
- **Queda de nó com lote:** um nó que cai deixa para expirar a posse dos jobs que havia adquirido — até o tamanho do lote, não apenas um. Eles voltam a ser elegíveis quando o lease vence, como sempre; muda a quantidade, não o mecanismo. Como o lote é limitado pelas vagas livres, o número é da ordem da concorrência configurada do nó.

## Shutdown (drain)

1. Para de aceitar novos jobs.
2. Em execução: terminam até `ShutdownDrainTimeout` (30s default); excedentes recebem cancelamento cooperativo.
3. Sinalizados-mas-não-iniciados: descartados localmente — a posse expira e **outro nó (ou o restart) reprocessa**. Nada se perde no shutdown normal.

## Relógio e fusos

- Relógio via `TimeProvider` (testável); tempos persistidos em UTC.
- Fuso default: **UTC**; `NoFusoHorario` aceita ids IANA e Windows nos dois sistemas (conversão nativa).
- DST: horário local **inexistente** → dispara imediatamente após a transição; **ambíguo** → primeira ocorrência. (Cron próprio — spec 005.)
- Multi-nó com relógios dessincronizados: leases têm margem; a elegibilidade usa o `now` injetado do nó que consulta.

## Eventos e observabilidade

- Eventos internos são **best-effort em processo**: falha de um handler não afeta os demais nem o job; entrega durável/entre nós é do `Guara.Distributed` (opt-in).
- `StateHistory` (opcional, default ligado) registra transições para a timeline do dashboard, com retenção própria.
