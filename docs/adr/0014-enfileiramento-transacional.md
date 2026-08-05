# ADR-0014 — Enfileiramento Transacional

- **Status:** Aceito
- **Data:** 2026-08-05

## Contexto

`IStorage.BeginTransactionAsync` existe desde o primeiro desenho do contrato de storage. Hoje, com cinco providers implementados, ele **não tem um único chamador**: os cinco lançam `NotSupportedException`, todos declaram `SupportsTransactions: false`, e nada no framework o invoca.

Isso não é implementação atrasada — é sintoma de um contrato desenhado para o consumidor errado.

**O Guará não precisa de transação para si.** "O storage é a fila": um job é uma linha, e cada operação já é atômica sozinha. `AcquireNextDueAsync` é um `UPDATE` único com lease; `ScheduleRetryAsync` é um `UPDATE` único. O Hangfire precisa de `IWriteOnlyTransaction` porque espalha o estado de um job entre set, hash e lista, e precisa movê-los juntos. O Guará não espalha, então nunca teve o que agrupar.

**Quem precisa é o usuário**, e para uma coisa só:

> Gravo o pedido e enfileiro o e-mail de confirmação. Ou os dois acontecem, ou nenhum.

Sem isso, os dois modos de falha são reais e silenciosos. Se a transação do negócio faz rollback depois do enfileiramento, um worker processa um job cujo pedido nunca existiu. Se o commit passa e o enfileiramento falha logo depois, o e-mail nunca sai e ninguém percebe. É o pedido número um em job scheduler.

**E o contrato atual não atende esse pedido.** `BeginTransactionAsync()` faz o *Guará* abrir a transação, mas `IGuaraClient.EnfileirarAsync` não aceita transação nenhuma — não há como enfileirar dentro da que foi aberta. É uma porta sem corredor atrás. Pior: mesmo que houvesse, a transação seria da conexão do Guará, enquanto o dado do negócio vive na conexão do `DbContext` do usuário.

A decisão precisa sair antes do congelamento da API pública: depois do 1.0, mudar a forma de um membro de `IStorage` quebra todo mundo que escreveu provider próprio.

## Decisão

O enfileiramento passa a poder participar de uma transação **do chamador**. O usuário abre, o Guará escreve dentro.

### Direção: o chamador é o dono

```csharp
await using var tx = await db.Database.BeginTransactionAsync(ct);

db.Pedidos.Add(pedido);
await db.SaveChangesAsync(ct);

await client.EnfileirarAsync(job, GuaraTransacoes.De(db.Database.GetDbTransaction()), ct);

await tx.CommitAsync(ct);
```

A alternativa — o Guará abrir e o usuário entrar com `UseTransaction` — custaria menos código nos providers, mas obriga a inverter o trecho que a pessoa já tem escrito. Uma funcionalidade que exige reescrever o caminho feliz para ser adotada não é adotada, e voltaríamos a ter um membro de contrato sem chamador, só que agora com implementação em quatro bancos.

### O contrato

`Guara.Abstractions` ganha um handle opaco, e `IGuaraClient` sobrecargas que o aceitam:

```csharp
public interface IGuaraTransaction;

ValueTask<JobId> EnfileirarAsync(JobDescriptor job, IGuaraTransaction transacao, CancellationToken ct = default);
ValueTask<JobId> AgendarAsync(JobDescriptor job, TimeSpan atraso, IGuaraTransaction transacao, CancellationToken ct = default);
```

O handle é opaco de propósito. Dar-lhe um membro significaria expor `System.Data.Common` em `Guara.Abstractions` — o que assumiria banco relacional numa camada que também serve o MongoDB. Quem interpreta o handle é o provider, que tem todo o direito de conhecer a própria tecnologia: ele converte para o tipo que emitiu e recusa o resto com mensagem clara.

`Guara.Storage` fornece a implementação compartilhada pelos providers relacionais, para que os quatro não a dupliquem:

```csharp
public sealed class RelationalTransaction(DbTransaction transaction) : IGuaraTransaction;
```

### `BeginTransactionAsync` e `ITransaction` são removidos

Com o chamador dono da transação, o Guará nunca precisa abrir uma. Manter os dois membros seria congelar no 1.0 exatamente aquilo que este ADR identificou como problema: superfície pública que ninguém exercita, e que todo autor de provider externo precisa escrever só para lançar exceção.

Se o `Guara.Pro.Batches` mais tarde precisar enfileirar N jobs atomicamente sem o usuário ter transação própria, o membro volta como **default interface member** — a plataforma suporta desde o `net8.0`, que é o TFM mínimo, e um membro com corpo padrão não quebra implementação existente. O caminho de volta é barato; o de ida, não.

### O que fica atômico, e o que não

O Guará **nunca** commita nem faz rollback da transação do chamador. Ele só escreve dentro dela; o controle é de quem abriu, do começo ao fim.

O `JobId` é devolvido antes do commit. Se o chamador desfizer a transação, aquele id nunca existiu — persistir o id fora da transação (log, resposta HTTP, outra tabela em outra conexão) é responsabilidade de quem o faz.

**O aviso de fila não sai.** [ADR-0012](0012-wakeup-por-sinal-de-fila.md) fez o enfileiramento avisar a fila para o dispatcher acordar na hora. Num enfileiramento transacional isso seria errado: o Guará não enxerga o commit do chamador, então avisaria antes de o job ser visível — e acordaria o dispatcher para buscar um job que ainda não existe, ou que vai desaparecer no rollback. O caminho transacional troca latência por atomicidade: o job entra no próximo ciclo de busca.

### Capacidade honesta por provider

`StorageCapabilities.SupportsTransactions` passa a significar "aceita enfileiramento dentro de uma transação do chamador".

| Provider | Suporta | Porquê |
|---|---|---|
| PostgreSQL, SQL Server, MySQL | ✅ | `DbTransaction` sobre a mesma conexão que alcança as tabelas do Guará |
| MongoDB | ❌ | Transação multi-documento exige **replica set**; declarar suporte quebraria quem roda standalone |
| In-Memory | ❌ | Não há transação a compartilhar |

Provider que declara `false` lança `NotSupportedException` na sobrecarga transacional, com mensagem que diz qual provider e por quê. O mesmo vale para handle de outra família — passar um `RelationalTransaction` a um provider que não é relacional é erro de composição, não de execução silenciosa.

**A transação do chamador precisa estar na conexão que alcança as tabelas do Guará**, o que significa Guará e aplicação no mesmo banco. Já é suportado e documentado: o isolamento por schema (PostgreSQL, SQL Server) e por prefixo (MySQL, MongoDB) existe para permitir exatamente essa convivência.

### Escopo em 1.0

Só `EnfileirarAsync` e `AgendarAsync`. São as operações que o usuário emite junto com uma escrita de negócio. `ExcluirAsync`, recorrentes, calendários e continuações são operações administrativas ou de fluxo interno — ninguém as chama dentro de uma transação de negócio, e sobrecarregá-las agora seria inflar a superfície congelada sem consumidor.

## Consequências

**Ganhos:** o pedido número um de quem vem do Hangfire passa a ser atendido; a superfície pública **diminui** às vésperas do congelamento, em vez de crescer com membros nunca exercitados; `SupportsTransactions` deixa de ser `false` decorativo e passa a informar algo verdadeiro e verificável pelo kit de conformidade.

**Custos:** os providers relacionais precisam saber executar seus comandos numa conexão emprestada, e não só na própria — é o preço da direção escolhida; o enfileiramento transacional não emite aviso de fila, então paga a latência do ciclo de busca; e o usuário precisa colocar o Guará no mesmo banco da aplicação para usar a funcionalidade, o que é uma escolha de topologia que nem todo mundo quer fazer.

Relaciona-se a [ADR-0003](0003-abstracao-de-storage-por-provider.md) (abstração por provider) e [ADR-0012](0012-wakeup-por-sinal-de-fila.md) (o aviso que este caminho deliberadamente não emite). Supera a parte do contrato de storage que previa transação iniciada pelo Guará.
