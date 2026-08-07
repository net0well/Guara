# Migração do Hangfire para o Guará

Guia para quem já roda Hangfire e está avaliando o Guará. Começa pela diferença que mais surpreende, porque ela muda a forma de escrever job — e se ela for aceitável, o resto é tradução direta.

## 1. A diferença que importa: não existe lambda

No Hangfire você enfileira passando uma expressão:

```csharp
BackgroundJob.Enqueue(() => _servico.GerarRelatorioAsync(clienteId));
```

No Guará, o método é **marcado** e o enfileiramento usa a fábrica que o compilador gera a partir dele:

```csharp
[GuaraJob]
public Task GerarRelatorioAsync(int clienteId, CancellationToken ct) { … }

// em qualquer lugar, com IGuaraClient injetado:
await jobs.EnfileirarAsync(RelatorioServiceGuara.GerarRelatorioAsync(clienteId), ct);
```

A fábrica se chama `{Tipo}Guara` e repete o nome do método **exatamente**, sufixo `Async` incluído. O `CancellationToken` não entra na chamada: o Guará o fornece na execução.

### Por que não tem lambda

Capturar `() => servico.Metodo(x)` como algo executável exige `Expression<T>`, e transformar uma `Expression` em chamada exige `Compile()` (emissão de IL em runtime) ou `MethodInfo.Invoke`. As duas são reflection em runtime, que quebra Native AOT — a coisa que o Guará promete. Detalhe em [ADR-0020](adr/0020-enfileiramento-por-fabrica-tipada.md).

### O que você ganha em troca

O Hangfire descobre problemas de assinatura **quando o job roda**. O Guará descobre **quando você compila**:

| Situação | Hangfire | Guará |
|---|---|---|
| Argumento não serializável | falha no worker | `GUARA0102` na build |
| Job genérico | falha no worker | `GUARA0105` na build |
| Retorno não suportado | falha no worker | `GUARA0106` na build |
| `CancellationToken` fora de posição | ignorado | `GUARA0107` na build |
| Renomeou o método | a expressão acompanha | a fábrica é reemitida |

### O que você perde

`F12` sobre o método deixa de ser direto: você navega para a fábrica gerada e de lá para o método real — um salto a mais.

## 2. Tradução da API

Os métodos de operação de jobs são em português por decisão de identidade ([ADR-0010](adr/0010-api-do-usuario-em-portugues.md)). Tipos, DI, options e rotas seguem o inglês do ecossistema.

| Hangfire | Guará |
|---|---|
| `BackgroundJob.Enqueue(...)` | `jobs.EnfileirarAsync(descritor, ct)` |
| `BackgroundJob.Schedule(..., delay)` | `jobs.AgendarAsync(descritor, delay, ct)` |
| `BackgroundJob.ContinueJobWith(id, ...)` | `jobs.ContinuarComAsync(id, descritor, ct: ct)` |
| `BackgroundJob.Delete(id)` | `jobs.ExcluirAsync(id, ct)` |
| `RecurringJob.AddOrUpdate(id, ..., cron)` | `jobs.AdicionarOuAtualizarRecorrenteAsync(job => job.ComId(id).Executa(descritor).ComCron(cron), ct)` |
| `IBackgroundJobClient` injetado | `IGuaraClient` injetado |

O estático global não tem equivalente: `IGuaraClient` vem sempre por injeção de dependência. Não há factory estática no Guará, por decisão de arquitetura.

## 3. Atributos

| Hangfire | Guará |
|---|---|
| `[Queue("emails")]` | `[GuaraFila("emails")]` |
| `[AutomaticRetry(Attempts = 3)]` | `[GuaraRetentativas(3)]` |
| `[AutomaticRetry(Attempts = 0)]` | `[GuaraRetentativas(0)]` |
| `[DisableConcurrentExecution(n)]` | `[GuaraDesabilitarConcorrencia]` |
| `[SkipWhenPreviousJobIsRunning]` | `[GuaraPularSeAnteriorEmExecucao]` |
| — | `[GuaraTempoLimite(segundos)]` |

Os atributos são lidos **em compilação** pelo source generator, não por reflection no worker.

## 4. Composição

```csharp
// Hangfire
services.AddHangfire(c => c.UsePostgreSqlStorage(cs));
services.AddHangfireServer();
app.UseHangfireDashboard("/hangfire");
```

```csharp
// Guará
services.AddGuara()
    .UsePostgreSqlStorage(cs)
    .AddGuaraJobs()          // registra os [GuaraJob] deste assembly, gerado em compilação
    .AddGuaraServer()        // executa os jobs neste processo
    .AddGuaraDashboard();

app.MapGuaraDashboard();     // por padrão em /guara
```

`AddGuaraJobs()` não faz varredura de assembly: o generator já produziu o registro.

## 5. Diferenças de comportamento que valem conferir

- **O dashboard nega acesso anônimo por padrão.** No Hangfire, `UseHangfireDashboard` só permite local por padrão e muita gente afrouxa isso em produção sem perceber. No Guará é preciso configurar autenticação explicitamente para que alguém entre — ver `UseGuaraAuthentication`.
- **Retentativa é persistente.** Falha com tentativas restantes grava `Retrying` reagendado com back-off, em vez de manter estado em memória.
- **Tempo limite é cooperativo.** `[GuaraTempoLimite]` cancela o `CancellationToken`; um job que ignora o token e completa termina como `Succeeded`, com aviso. Não há aborto de thread.
- **Recorrentes sobrepõem por padrão**, e N disparos perdidos viram **uma** compensação, nunca backfill. Para não sobrepor, use `[GuaraPularSeAnteriorEmExecucao]`.
- **Prioridade de fila é estrita.** A ordem em `DispatcherOptions.Queues` é respeitada de verdade, o que significa que uma fila de baixa prioridade pode ficar sem vez enquanto houver trabalho acima. É comportamento documentado, não defeito — ver [semantics.md](semantics.md).

## 6. Migração dos dados

Não há importador de dados do Hangfire, e não deve haver: os esquemas não se correspondem, e migrar jobs pendentes entre dois agendadores é o tipo de operação que falha em silêncio.

O caminho recomendado é rodar os dois lado a lado durante a transição:

1. Suba o Guará com seu próprio schema (ou prefixo de tabela) no mesmo banco.
2. Aponte o **enfileiramento novo** para o Guará; deixe o servidor do Hangfire ligado até drenar o que já estava lá.
3. Recorrentes: recadastre no Guará e remova do Hangfire, um a um.
4. Quando o Hangfire não tiver mais trabalho pendente, desligue-o.

O isolamento por schema e por prefixo existe para esse período de convivência.

## 7. O que o Guará ainda não tem

Honestidade antes da migração — o que existe no Hangfire e aqui não:

- **Batches** (grupos com callback de conclusão): planejado como pacote comercial, não no 1.0.
- **Exporters OpenTelemetry** prontos: o Guará emite `ActivitySource` e `Meter` nativos, mas o pacote de exporter ainda não saiu.
- **CLI**: planejada.

O que o Guará tem e o Hangfire não: zero reflection com AOT, quatro storages sob o mesmo kit de conformidade, calendários de exclusão, fuso IANA/Windows nativo nos dois sistemas, e o dashboard em tempo real por SSE em vez de polling.
