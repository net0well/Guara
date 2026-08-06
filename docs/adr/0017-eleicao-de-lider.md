# ADR-0017 — Eleição de Líder

- **Status:** Aceito
- **Data:** 2026-08-05

## Contexto

O Guará já roda em vários nós: a posse de job é garantida por lease, o lock distribuído vale entre nós em quatro providers, e os laços coordenados do `GuaraServer` — promoção de recorrentes e manutenção — já rodam sob lock para que apenas um nó execute cada ciclo.

Mas a coordenação é implícita, e tem um furo concreto:

```csharp
await using var recurringLock = await _storage.Locks.TryAcquireAsync(
    RecurringLockKey, _options.RecurringPollInterval, ct);
```

**O lock nasce com validade de um ciclo e nunca é renovado.** Se a promoção demorar mais que o intervalo — muitos recorrentes vencidos, banco lento, pausa de GC —, a posse expira **no meio do trabalho**, outro nó a adquire e passa a promover os mesmos recorrentes. A janela é estreita, porque cada promoção atualiza o `NextRunAt` e tira o recorrente da lista de vencidos, mas ela existe: dois nós promovendo a mesma definição no mesmo instante geram ocorrência duplicada.

Isso precisa ser resolvido antes do congelamento da API por um motivo além do bug: se a solução exigir mudança em `ILockProvider`, ela ricocheteia nos cinco providers e no kit de conformidade.

## Decisão

A coordenação entre nós vira um conceito explícito, `ILeaderElection`, em `Guara.Abstractions`:

```csharp
public interface ILeaderElection
{
    ValueTask<ILeadership?> TryAcquireAsync(string role, CancellationToken ct);
}

public interface ILeadership : IAsyncDisposable
{
    string Role { get; }
    CancellationToken Lost { get; }
}
```

A implementação vive em **`Guara.Cluster`**, sobre `ILockProvider`, e faz o que faltava: **renova a posse enquanto o trabalho acontece** e, quando a renovação falha, cancela `Lost`. Quem lidera passa `Lost` ao trabalho que está executando, e o trabalho para no instante em que a liderança cai.

O `GuaraServer` deixa de adquirir lock direto e passa a pedir liderança pelos papéis `recurring` e `maintenance`.

### Fencing token: não entra, e o motivo importa

A suspeita que abriu este trabalho era que `ILockHandle` precisaria de um **fencing token** — um número monotônico que o recurso verifica, para barrar um líder que já perdeu a posse mas ainda não percebeu.

Não entra, porque um fencing token só serve se **o recurso o verifica**. Aqui o recurso é o próprio storage: seria preciso que `IRecurringStorage.UpsertAsync`, a purga e as demais escritas recebessem o token e recusassem o que viesse com um token velho. Isso é mudança em todo o contrato de escrita, nos cinco providers — muito além do problema que existe.

E o projeto já resolve exatamente esta classe de problema de outro jeito, em produção e coberto por testes: **posse de job**. O worker renova o lease e, quando `RenewLeaseAsync` devolve `false`, aborta a execução local para nunca processar em dobro. Mesma falha, mesma mitigação. Liderança passa a usar o mesmo padrão, por coerência e porque ele já provou funcionar.

**Consequência para o congelamento: `ILockProvider` e `ILockHandle` não mudam.** Era o que este trabalho precisava descobrir.

### Onde o pacote vive

`Guara.Server` passa a referenciar `Guara.Cluster`, e `AddGuaraServer()` registra a eleição por lock. Quem liga o servidor recebe a coordenação correta sem precisar saber que ela existe — não há configuração nova para acertar, e não há caminho de código alternativo para o caso de o pacote não estar presente.

### O que fica de fora

Descoberta de nó no painel, políticas de failover configuráveis e `Guara.Distributed` são aditivos: não tocam contrato publicado e cabem depois do congelamento.

## Consequências

**Ganhos:** a janela de duplicação em ciclo longo fecha; a coordenação entre nós deixa de ser efeito colateral de um lock e vira conceito nomeado, com teste próprio; e o congelamento da API ganha a certeza de que `ILockProvider` está no formato final.

**Custos:** mais um pacote e mais um contrato em `Guara.Abstractions`; e quem liderar passa a manter uma tarefa de renovação em segundo plano por papel, que é custo pequeno e constante.

**Aceito conscientemente:** sem fencing token, um nó em pausa longa de GC pode, em teoria, agir por um instante depois de a posse ter expirado. É a mesma exposição que a posse de job já tem desde sempre, e fechá-la exigiria que todo caminho de escrita do storage verificasse token.

Relaciona-se a [ADR-0003](0003-abstracao-de-storage-por-provider.md) (contratos de storage) e a [semantics.md](../semantics.md) (garantias de entrega).
