# ADR-0018 — `Guara.Distributed` Não Será Criado

- **Status:** Aceito
- **Data:** 2026-08-05

## Contexto

O catálogo de componentes previa `Guara.Distributed`, descrito em [components.md](../components.md) apenas como "coordenação distribuída" — que é a mesma frase que descreve o `Guara.Cluster`, implementado em [ADR-0017](0017-eleicao-de-lider.md).

Olhando só por ali, o pacote seria um nome sem conteúdo. Mas outros dois documentos lhe atribuem responsabilidades reais:

- [semantics.md](../semantics.md): dedupe de enfileiramento por `IdempotencyKey`, "via `Guara.Distributed` (opt-in, spec 026)".
- [semantics.md](../semantics.md) e [spec 002](../../spec/002-guara-core.md): entrega **durável e entre nós** de eventos internos, em contraste com o fan-out best-effort em processo.

Ou seja: o pacote não estava vazio, estava servindo de gaveta para duas coisas que não têm relação entre si nem com o nome dele.

## Decisão

`Guara.Distributed` **não será criado**, e sai do catálogo. As duas responsabilidades são separadas e tratadas pelo que realmente são.

### Coordenação distribuída já existe

É o `Guara.Cluster`: eleição de líder com posse renovada sobre o lock distribuído do storage. Manter um segundo pacote com a mesma descrição só criaria a dúvida de qual instalar.

### Dedupe de enfileiramento é assunto de storage, não de coordenação

Impedir que o mesmo trabalho entre duas vezes é uma **restrição de unicidade** sobre uma chave — o storage já é a fila, e é ele quem sabe recusar duplicata de forma atômica. Não precisa de coordenação entre nós: precisa de um índice único.

Fica registrado como funcionalidade pendente do próprio contrato de storage, não de um pacote novo. Quando entrar, entra como chave opcional no enfileiramento.

### Entrega durável de eventos entre nós sai de escopo

Os eventos internos do Guará (`JobCreated`, `JobCompleted`) são **notificação**, não fonte da verdade. Quem garante que o trabalho acontece é o storage com posse e lease; quem consome os eventos hoje é o painel, para atualizar em tempo real.

Tornar essa notificação durável e entre nós é construir um barramento de mensagens — outro produto, com outras garantias, e em concorrência direta com o que o usuário provavelmente já tem. O Guará continua entregando evento **best-effort em processo**, e quem precisa de evento durável publica do próprio job, no barramento que já usa.

O aviso de fila entre nós, que é a única notificação com valor operacional real, já existe em [`Guara.Redis`](0013-redis-como-acelerador.md) — e é explicitamente best-effort, com o ciclo de busca como piso.

## Consequências

**Ganhos:** o catálogo deixa de prometer um pacote sem conteúdo próprio; as duas responsabilidades reais vão para onde pertencem, uma como pendência de storage e outra fora de escopo; e some a dúvida entre `Cluster` e `Distributed`.

**Custos:** `semantics.md` e a spec 002 apontavam para esse pacote como resposta a duas perguntas legítimas — "como evito enfileirar duas vezes?" e "meus eventos sobrevivem a uma queda?". As respostas mudam de "vem no `Guara.Distributed`" para, respectivamente, "pendente no contrato de storage" e "não é o que o Guará faz". A segunda é menos confortável, e é honesta.

Segue o mesmo raciocínio de [ADR-0013](0013-redis-como-acelerador.md) (`Guara.Storage.Redis` cancelado) e [ADR-0014](0014-enfileiramento-transacional.md) (`BeginTransactionAsync` removido): item planejado que, na hora de construir, não se sustenta sai do catálogo com o motivo escrito, em vez de virar pacote publicado que ninguém consegue remover depois.
