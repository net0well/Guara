# Anti-Padrões

O que IAs e desenvolvedores novos costumam fazer errado no Guará. Vários destes são detectados por `Guara.Analyzers` e **quebram a build**.

1. **Misturar responsabilidades num pacote** — `Storage + Scheduler`, `Scheduler + Worker`, `Storage + Dashboard`. Cada responsabilidade é um projeto. Ver [components.md](components.md).

2. **Motor referenciando provider concreto** — `Scheduler` chamando `SqlServerStorage`. Sempre contra `IStorage`/`IJobStorage`. (`GUARA0002`)

3. **Dependência invertida** — um componente de baixo (ex.: `Abstractions`) referenciando um de cima (ex.: `Core`). A seta é sempre `Dashboard → Api → Core → Abstractions`. (`GUARA0001`) Ver [dependency-rules.md](dependency-rules.md).

4. **Chamada direta entre componentes** — `Dispatcher` chamando `Worker.RunAsync(...)`. Componentes se comunicam por **evento** ou **contrato**, nunca por referência concreta. Ver [ADR-0002](adr/0002-comunicacao-por-eventos.md).

5. **`Guara.Core` conhecendo banco, ASP.NET ou Dashboard** — `Core` só conhece `Abstractions`. Se precisou de um `using` de EF Core ou ASP.NET em `Core`, o desenho está errado.

6. **Mais de um método público de entrada por pacote** — cada pacote expõe **um** `AddGuara...()`/`Use...()`. Vários pontos de entrada geralmente significam dois componentes. Ver [naming-conventions.md](naming-conventions.md).

7. **Registro manual de DI espalhado** — `services.AddSingleton<IScheduler, Scheduler>()` na aplicação. O registro vive **apenas** dentro do `AddGuara...()` do pacote.

8. **Factory global estática ou singleton estático** — proibido. Estado compartilhado global impede testabilidade, thread-safety previsível e AOT. Tudo por DI.

9. **Reflection em runtime** — descoberta/registro deve usar Source Generators. Reflection quebra AOT/Trimming e aloca. Ver [performance.md](performance.md) e [ADR-0005](adr/0005-source-generators-para-registro.md).

10. **`Task` em API de caminho crítico onde cabe `ValueTask`** — hot paths (aquisição de job, publicação de evento) usam `ValueTask`.

11. **Ignorar `CancellationToken`** — toda API assíncrona recebe e propaga o token. Exceção: persistência de estado final **após** efeito colateral externo já concluído. Ver [execution-flows.md](execution-flows.md).

12. **Implementação pública quando poderia ser `internal`** — a classe do provider/motor deve ser `internal sealed`; o mundo externo depende do contrato em `Abstractions`.

13. **`Dashboard.Api` renderizando HTML** — a API só entrega dados; a UI é `Guara.Dashboard.React`, que consome apenas a API.

14. **Alocar em loop quente sem Object Pool** — buffers/contexto de curta duração em caminho quente devem sair de um pool. Ver [performance.md](performance.md).

15. **Bloquear thread (`.Result`, `.Wait()`, `Thread.Sleep`)** — proibido no runtime. Assíncrono de ponta a ponta; espera temporal via `Task.Delay(..., ct)`.
