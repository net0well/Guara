# ADR-0013 — Redis como Acelerador, não como Storage

- **Status:** Aceito
- **Data:** 2026-08-05

## Contexto

O catálogo de componentes previa `Guara.Storage.Redis`, ao lado dos providers relacionais e do MongoDB. Ao chegar a vez de implementá-lo, duas propriedades do Redis colidiram com o que o Guará promete.

**Durabilidade.** Um scheduler não pode perder job. A persistência do Redis é assíncrona por natureza: RDB perde tudo desde o último snapshot, e o AOF em `everysec` perde até um segundo de escritas. `appendfsync always` fecha a janela ao custo de um fsync por comando, o que destrói justamente a vantagem que motivaria usá-lo. Um provider que perde jobs numa queda não é um provider de storage do Guará — é uma pegadinha para quem o escolher por ser rápido.

**Consulta.** O painel filtra por estado, fila, tipo, texto e período, com paginação, contagem total e percentil de latência por balde. Sobre um banco relacional ou o MongoDB isso é uma consulta com índice. Sobre Redis vira meia dúzia de índices secundários mantidos à mão em `SET`/`ZSET`, atualizados em cada transição de estado — e sem transação que os mantenha coerentes entre si, um crash no meio de uma transição deixa o painel mentindo.

Ao mesmo tempo, o Redis é excelente naquilo que falta ao Guará: entregar uma notificação a todos os nós em milissegundos. Com [ADR-0012](0012-wakeup-por-sinal-de-fila.md) o aviso de trabalho virou um contrato (`IQueueSignal`) com implementação em processo, e o alcance entre nós é exatamente o buraco que o Redis preenche bem.

## Decisão

`Guara.Storage.Redis` **não será implementado** e sai do catálogo de componentes.

No lugar entra **`Guara.Redis`**, um pacote **acelerador**: ele não guarda estado nem participa da verdade durável, apenas leva o aviso de fila entre nós por pub/sub. Selecionado por `UseRedis(...)`, que substitui o sinal em processo registrado por `AddGuara()`. O storage continua sendo o que já é.

Três consequências de projeto seguem daí:

1. **Nada nele é durável, e nada precisa ser.** O aviso é best-effort e o ciclo periódico do dispatcher é o piso. Perder uma mensagem de pub/sub — o Redis não a guarda para quem não estava assinando — atrasa uma busca, não perde um job.

2. **O aviso local sai antes da rede.** Quem publica também avisa o portão em processo, então o nó que enfileirou acorda mesmo com o Redis fora do ar. A publicação carrega o id do nó de origem para que ele descarte o próprio eco.

3. **Lock distribuído e cache de leitura ficam de fora**, apesar de o Redis ser bom nos dois. Os quatro storages de produção já declaram `SupportsDistributedLock: true` com posse por registro com validade e dono, então um lock em Redis só serviria a quem usa o storage em memória — que não é compartilhado entre nós de qualquer forma. O cache de leitura do painel depende de uma política de invalidação que ainda não existe; entregá-lo agora seria um painel que mostra o passado.

Esta decisão **emenda o [ADR-0009](0009-politica-de-dependencias.md)**, cujo item 2 permitia `StackExchange.Redis` exclusivamente dentro de `Guara.Storage.*`: o driver passa a ser permitido em `Guara.Redis`, e continua proibido em todo o resto. `Guara.Redis` fica na camada dos providers do `Guara.Analyzers` — motores que o alcançarem quebram a build com `GUARA0002`, como já acontece com qualquer provider concreto.

## Consequências

**Ganhos:** o Guará deixa de prometer um storage que não conseguiria honrar; quem já tem Redis na infraestrutura ganha despacho entre nós em milissegundos sem trocar de banco nem baixar o intervalo de busca; o pacote é pequeno e sem estado, então falha nele degrada para o comportamento anterior em vez de derrubar nada.

**Custos:** quem esperava `Guara.Storage.Redis` pela leitura do catálogo precisa de uma explicação — que passa a viver no README e neste ADR; mais um pacote a versionar e publicar; e a dependência de `Guara.Redis` em `Guara.Core` (para reaproveitar o portão de espera em processo) é a primeira de um pacote de tecnologia sobre o núcleo, aceita por evitar duplicar a lógica de retenção e despertar.

Relaciona-se a [ADR-0003](0003-abstracao-de-storage-por-provider.md) (abstração por provider) e [ADR-0012](0012-wakeup-por-sinal-de-fila.md) (o contrato que ele implementa).
