# ADR-0010 — API Voltada ao Usuário em Português

- **Status:** Aceito
- **Data:** 2026-07-16

## Contexto

Guará é um projeto brasileiro, com nome, identidade e documentação em português. A superfície que o desenvolvedor usa no dia a dia — enfileirar, agendar, encadear jobs — é parte dessa identidade. Ao mesmo tempo, o framework precisa se integrar naturalmente ao ecossistema .NET, cujas convenções (extensões `Add*/Use*`, sufixo `Async`, nomes de tipos) são universais em inglês.

## Decisão

A **API de operação de jobs — os métodos que o usuário chama — é em português**. O restante segue as convenções do ecossistema .NET em inglês.

### Em português (superfície do usuário)

Métodos de `IGuaraClient` (e do `IBatchClient` no tier Pro):

| Operação | Método |
|---|---|
| Fire-and-forget | `EnfileirarAsync(...)` |
| Com atraso | `AgendarAsync(..., TimeSpan)` |
| Recorrente (upsert) | `AdicionarOuAtualizarRecorrenteAsync(id, ..., cron, tz)` |
| Excluir | `ExcluirAsync(jobId)` |
| Continuation | `ContinuarComAsync(paiId, ...)` |
| Batch (Pro): criar / continuar / status | `CriarAsync` / `ContinuarBatchComAsync` / `ObterStatusAsync` |

### Em inglês (convenções do ecossistema — não mudam)

- **Sufixo `Async`** — convenção .NET para métodos assíncronos; mantido mesmo nos métodos em português.
- **Extensões de DI** — `AddGuara()`, `AddGuaraServer()`, `UsePostgreSqlStorage()`, `MapGuaraDashboard()` ([ADR-0006](0006-uma-extensao-addguara-por-pacote.md): integração idiomática com `Microsoft.Extensions.DependencyInjection`).
- **Nomes de tipos e contratos** — `IGuaraClient`, `JobId`, `JobState`, `IJobMiddleware`, eventos (`JobCreated`, `JobCompleted`), options (`WorkerOptions.MaxConcurrency`), atributos (`[GuaraJob(MaxAttempts = 0)]`).
- **Rotas HTTP, strings de permissão (`guara:view`), comandos da CLI, chaves de configuração** — superfícies técnicas/scriptáveis, interoperáveis com tooling.
- **Contratos internos entre componentes** — não são superfície do usuário.

## Consequências

**Ganhos:** identidade brasileira forte e diferenciada; API do dia a dia natural para o público primário; documentação PT-BR e código falam a mesma língua.

**Custos:** desenvolvedores não lusófonos precisam da tabela de tradução (mitigado: README EN documenta cada método com sua semântica); mistura de idiomas na mesma linha de código (`await jobs.EnfileirarAsync(...)` dentro de um `AddGuara()`), assumida conscientemente.

Regras detalhadas em [naming-conventions.md](../naming-conventions.md). Specs afetadas: 005, 019, 029, 030, 031.
