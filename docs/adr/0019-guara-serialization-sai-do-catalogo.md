# ADR-0019 — `Guara.Serialization` Sai do Catálogo

- **Status:** Aceito
- **Data:** 2026-08-06

## Contexto

`Guara.Serialization` foi especificado na spec 003, implementado, testado, medido por uma suíte de benchmarks própria e **publicado no NuGet nos três previews** (`0.1.0-preview.1`, `.2` e `.3`).

Nada no framework o chama.

O levantamento antes do congelamento da API encontrou o seguinte:

| O que precisa virar bytes | Por onde passa hoje | Usa `ISerializer`? |
|---|---|---|
| Argumentos do job | `{job}Args.Write/Read`, emitido pelo source generator | não |
| `JobDescriptor` | um `JsonSerializerContext` por provider (`PostgreSqlJsonContext`, `SqlServerJsonContext`, `MySqlJsonContext`, `MongoDocuments`) | não |
| Recorrentes e calendários | idem, por provider | não |
| Resultado do job | não existe: `ReturnKind` aceita `void`, `Task` e `ValueTask` — job não devolve valor | não |

E o pacote publicado é pior do que "sem consumidor interno": **sua API pública inteira é uma classe de exceção.** `SystemTextJsonSerializer` é `internal`, e não existe `AddGuaraSerialization()`. Quem instalasse o pacote não teria como construir um serializador. Não é um ponto de extensão pouco usado — é um ponto de extensão que nunca foi ligado.

## Decisão

O pacote sai do catálogo. `ISerializer` sai de `Guara.Abstractions` junto, porque contrato público sem implementação alcançável é pior que ausência: promete extensibilidade que não existe.

### Por que Quartz e Hangfire precisam disso e o Guará não

A pergunta natural é se remover não deixa o Guará atrás dos concorrentes, já que os dois têm serializador plugável. A resposta é que **eles precisam dele justamente por fazerem o que o Guará escolheu não fazer.**

O `IObjectSerializer` do Quartz existe porque o `JobDataMap` carrega **objetos arbitrários do usuário**, descobertos em runtime; sem serializador plugável não há como persistir aquilo. O Hangfire deixa o serializador configurável porque descobre método e argumentos **por reflection** e precisa lidar com tipos que só aparecem em execução.

No Guará o generator conhece os tipos em compilação e emite leitor e escritor exatos para cada job. **O problema que justifica o ponto de extensão nos dois concorrentes foi projetado para fora daqui** — é consequência da mesma decisão que dá zero reflection e AOT-safe. Manter o pacote por paridade de catálogo seria copiar a solução de um problema que não temos.

Isso também explica a origem do pacote: `SerializeArgs` foi documentado para o caso em que "os tipos estáticos não são conhecidos no call site" — o caminho de lambda (`EnfileirarAsync(() => Metodo(x))`), que nunca foi construído e foi descartado por ser incompatível com AOT.

### O único uso legítimo, e por que não basta

Centralizar a serialização do `JobDescriptor`, hoje duplicada em quatro contextos por provider: se o formato mudar, são quatro lugares.

Mas isso é **refatoração interna**, não ponto de extensão público, e faria cada provider depender de `Guara.Serialization` — dependência lateral entre pacotes de mesma camada, que as regras deste repositório proíbem. Não sustenta um pacote publicado. Se a duplicação incomodar, resolve-se dentro de `Guara.Storage`, sem pacote novo.

### O que fazer com o que já está publicado

O NuGet não permite apagar versão publicada, só deslistar. Os três previews continuam existindo e continuam funcionando para quem os tenha referenciado — o que não quebra ninguém, porque o pacote não faz nada.

Na publicação do 1.0, as três versões são **deslistadas e depreciadas**. Sem pacote substituto na mensagem de depreciação: não há substituto porque não havia função.

## Consequências

**Ganhos:** um pacote a menos para versionar, documentar e sustentar por compatibilidade depois do congelamento; `Guara.Abstractions` perde um contrato que ninguém implementa fora do próprio repositório; e o congelamento passa a valer sobre a superfície que o framework realmente usa.

**Custos:** o nome `Guara.Serialization` fica permanentemente ocupado no nuget.org, e três versões deslistadas ficam no histórico público do projeto. É o preço de ter publicado antes de checar quem chamava — e a razão de o levantamento acontecer **antes** do 1.0, quando remover ainda é barato.

**Precedente:** mesma decisão de [ADR-0013](0013-redis-como-acelerador.md), [ADR-0014](0014-enfileiramento-transacional.md) e [ADR-0018](0018-guara-distributed-nao-existe.md) — item planejado que não se sustenta na hora de construir sai do catálogo com o motivo escrito, em vez de virar pacote publicado que não se remove depois. A diferença aqui é que este já tinha sido publicado, o que torna a regra mais cara e mais necessária.
