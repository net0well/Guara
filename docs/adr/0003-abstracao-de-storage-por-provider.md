# ADR-0003 — Abstração de Storage por Provider

- **Status:** Aceito
- **Data:** 2026-07-16

## Contexto

O Guará precisa persistir jobs, filas, locks e estado em backends muito diferentes (memória, SQL Server, PostgreSQL, Redis, Mongo). Amarrar o núcleo a um banco específico inviabilizaria a evolução e os cenários de teste.

## Decisão

Modelo de **provider** idêntico ao do EF Core: `Guara.Storage` **define** os contratos (`IStorage`, `IJobStorage`, `IQueueStorage`, `ITransaction`, `ILockProvider`) e **não implementa**; cada `Guara.Storage.{Tecnologia}` implementa apenas esses contratos.

Seleção do provider por uma única extensão fluente:

```csharp
builder.Services.AddGuara().UsePostgreSqlStorage(conn);
```

Trocar de backend = trocar uma linha; nenhum motor (`Scheduler`, `Dispatcher`, `Worker`, `Executor`) muda.

## Consequências

**Ganhos:** portabilidade entre bancos; `Guara.Storage.Memory` habilita testes rápidos e determinísticos; providers evoluem/publicam sozinhos; núcleo permanece livre de dependências de banco.

**Custos:** o contrato precisa ser o denominador comum expressivo o bastante para todos os backends; recursos específicos de um banco exigem extensões opcionais sem vazar para o contrato base.

Detalhado em [../patterns.md](../patterns.md) (seção Provider) e [../components.md](../components.md).
