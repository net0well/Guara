# Guará — app de exemplo

Um único processo que mostra o Guará de ponta a ponta: executa os jobs (servidor
embutido) **e** serve o dashboard em tempo real. Serve tanto de bancada de teste quanto
de referência de "como montar o Guará".

## Rodar

```bash
dotnet run --project samples/Guara.Host
```

No primeiro build a SPA do dashboard é construída automaticamente (requer Node 20.19+/22;
sem Node, o dashboard cai numa página placeholder). Depois abra:

- **Dashboard:** http://localhost:5080/guara — login de exemplo **admin / guara**

O painel abre com dados já semeados e atualiza **ao vivo** (SSE): relatórios sendo
gerados a cada poucos segundos, um recorrente a cada 10s, pagamentos que às vezes
falham e reentram (Retrying), uma conciliação que falha de vez (Failed), um job
agendado e uma continuação.

## O que o exemplo demonstra

- **Composição** (`Program.cs`): `AddGuara().UseConfiguration().UseGuaraDiagnostics().AddGuaraJobs()`
  + storage + `AddGuaraServer()` + `AddGuaraDashboard(...)` e `MapGuaraDashboard()`.
- **Jobs tipados** (`Jobs/DemoJobs.cs`): `[GuaraJob]` com `[GuaraFila]`, `[GuaraRetentativas]`,
  `[GuaraTempoLimite]` — enfileirados pelas factories geradas (`DemoJobsGuara.*`).
- **Recorrentes, agendados e continuações** (`Jobs/DemoSeeder.cs`).
- **Dashboard** com login próprio, filas, jobs, detalhe com ações (retentar/disparar/excluir),
  recorrentes, servidores e stream em tempo real.

## Configuração

Tudo pela seção `Guara` do `appsettings.json` (filas, polling, retenção, dashboard).

- **PostgreSQL:** preencha `Guara:Storage:PostgreSql:ConnectionString` para usar o banco
  em vez da memória (ex.: o Postgres do `Infra/`).
- **Login do dashboard:** `Guara:Dashboard:User` / `Guara:Dashboard:Password` (em produção,
  venha de variável de ambiente ou user-secrets — nunca literal).
