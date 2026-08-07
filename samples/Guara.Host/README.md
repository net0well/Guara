# Guará — app de exemplo

Uma API de pedidos que executa os jobs no próprio processo (servidor embutido) **e** serve
o dashboard em tempo real. É bancada de teste e referência de "como montar o Guará" — no
layout que um projeto real teria, e não uma demonstração solta.

## O fluxo

```
POST /api/pedidos
      │
      ▼
PedidosEndpoints ──► PedidoService ──► IPedidoRepository   (grava, responde 201)
                           │
                           ├─► EnfileirarAsync(PedidoJobsGuara.EnviarConfirmacaoAsync(id))
                           └─► EnfileirarAsync(PedidoJobsGuara.CobrarAsync(id))
                                    └─► ContinuarComAsync(… AvisarCobrancaRecusadaAsync(id))
```

A resposta sai assim que o pedido está gravado. Confirmação e cobrança já estão
enfileiradas, e quem chamou não espera por nenhuma das duas — que é o motivo de existir um
job scheduler.

| Pasta | Papel |
|---|---|
| `Endpoints/` | Entrada HTTP. Não conhece o Guará: chama o serviço |
| `Services/` | Regra de negócio. Decide o que vira job |
| `Repositories/` | Acesso a dados (em memória aqui; num projeto real, um `DbContext`) |
| `Models/` | Entidades |
| `Jobs/` | Trabalho de fundo, marcado com `[GuaraJob]` |

## Rodar

```bash
dotnet run --project samples/Guara.Host
```

No primeiro build a SPA do dashboard é construída automaticamente (requer Node 20.19+/22;
sem Node, o dashboard cai numa página placeholder). Depois abra:

- **Dashboard:** http://localhost:5080/guara — usuário **admin**

A senha **não** vem versionada. Se `Guara:Dashboard:Password` não estiver configurada, o
exemplo sorteia uma senha por execução e a imprime no log do boot (`Senha do dashboard não
configurada; gerada para esta execução: ...`). Para fixar uma:

```bash
dotnet user-secrets --project samples/Guara.Host set "Guara:Dashboard:Password" "<senha>"
```

O `GeradorDeTrafego` cria pedidos a cada poucos segundos pelo mesmo caminho que a API usa,
então o painel tem movimento sem ninguém chamar nada na mão: cobranças que às vezes falham
e reentram (Retrying), continuações disparando, e o relatório recorrente.

## Experimente

```bash
curl -X POST http://localhost:5080/api/pedidos \
  -H 'Content-Type: application/json' \
  -d '{"emailCliente":"ana@exemplo.com","total":149.90}'

# a situação começa em "Recebido"; segundos depois vira "Pago" (ou "Recusado")
curl http://localhost:5080/api/pedidos/1
```

## O que o exemplo demonstra

- **Composição** (`Program.cs`): `AddGuara().UseConfiguration().UseGuaraDiagnostics().AddGuaraJobs()`
  + storage + `AddGuaraServer()` + `AddGuaraDashboard(...)` e `MapGuaraDashboard()`.
- **Enfileiramento tipado** (`Services/PedidoService.cs`): a fábrica gerada
  `PedidoJobsGuara.CobrarAsync(id)` — **não** há lambda, e o nome da fábrica repete o do
  método, sufixo `Async` incluído ([ADR-0020](../../docs/adr/0020-enfileiramento-por-fabrica-tipada.md)).
- **Atributos declarativos** (`Jobs/`): `[GuaraFila]` separando e-mail de relatório,
  `[GuaraRetentativas]` onde a falha é transitória e `[GuaraRetentativas(0)]` onde repetir
  mandaria e-mail duplicado, `[GuaraTempoLimite]` e `[GuaraDesabilitarConcorrencia]`.
- **Continuações** encadeando o aviso de recusa ao desfecho da cobrança.
- **Jobs com DI**: `PedidoJobs` recebe o mesmo `IPedidoRepository` que os endpoints usam.
- **Dashboard** com login próprio, filas, jobs, detalhe com ações (retentar/disparar/excluir),
  recorrentes, servidores e stream em tempo real.

## AOT e trimming

O exemplo compila **sem exceção** com os analisadores de trimming e AOT ligados. Isso guiou
duas escolhas que valem para qualquer app que precise do mesmo:

- **Minimal API, não controllers MVC.** O MVC não suporta trimming nem Native AOT. O
  `EnableRequestDelegateGenerator` resolve o binding dos endpoints em compilação — é
  também por isso que o `Guara.Dashboard.Api`, sendo biblioteca, declara
  `IsAotCompatible=false`: o gerador só roda no app consumidor.
- **`PedidosJsonContext`** serializa os contratos HTTP sem reflection, pelo mesmo princípio
  que o Guará aplica aos argumentos de job.

Servir o painel embutido é o único caminho aqui que não é AOT — por desenho, e documentado.
Quem publica em Native AOT roda o Guará headless, sem o pacote do dashboard.

## Configuração

Tudo pela seção `Guara` do `appsettings.json` (filas, polling, retenção, dashboard).

- **PostgreSQL:** preencha `Guara:Storage:PostgreSql:ConnectionString` para usar o banco
  em vez da memória (ex.: o Postgres do `Infra/`).
- **Login do dashboard:** `Guara:Dashboard:User` no `appsettings.json`; a senha
  (`Guara:Dashboard:Password`) só por user-secrets ou variável de ambiente — o
  `appsettings.json` versionado carrega a chave vazia, nunca o valor.
