# Checklist Obrigatório — Novo Componente / Alteração

Execute antes de commitar qualquer componente novo ou alteração estrutural. Espelha [skill.md do exemplo] no formato: itens objetivos, verificáveis.

## 1. Responsabilidade e Estrutura

- [ ] O componente tem **uma única** responsabilidade — nada misturado ([components.md](components.md)).
- [ ] A responsabilidade não duplica a de um componente existente.
- [ ] Pacote nomeado `Guara.{Componente}` ou `Guara.{Contrato}.{Tecnologia}` para provider.
- [ ] Projeto de testes correspondente criado em `tests/`.
- [ ] Benchmark criado em `benchmarks/` se for caminho crítico.

## 2. Contratos e Dependências

- [ ] Interface principal (`I{Componente}`) definida em `Guara.Abstractions`.
- [ ] Implementação depende **apenas** de abstrações — nenhuma referência a implementação concreta de outro componente (`GUARA0002`).
- [ ] Dependências unidirecionais respeitadas: `Dashboard → Api → Core → Abstractions` (`GUARA0001`).
- [ ] `Guara.Core` não conhece banco, ASP.NET nem Dashboard.
- [ ] Implementação é `internal sealed` quando não precisa ser pública.

## 3. Comunicação

- [ ] Nenhuma chamada direta a outro componente — só evento ou contrato.
- [ ] Eventos nomeados no passado (`JobCompleted`) e definidos em `Guara.Abstractions`.
- [ ] Filas internas usam `Channel<T>`.

## 4. Injeção de Dependência e API Pública

- [ ] **Um único** `AddGuara...()` / `Use...()` no namespace `Microsoft.Extensions.DependencyInjection`.
- [ ] Nenhum `services.Add*<>()` manual fora desse método.
- [ ] Nenhuma factory global estática nem singleton estático.
- [ ] `{Componente}Options` dedicado + validação de configuração no startup.
- [ ] Extensão devolve `IGuaraBuilder` para manter a fluência.

## 5. Performance

- [ ] Zero reflection em runtime (descoberta/registro via Source Generators).
- [ ] APIs de caminho crítico usam `ValueTask`.
- [ ] `CancellationToken` recebido e propagado em toda API assíncrona.
- [ ] Sem `.Result` / `.Wait()` / `Thread.Sleep` no runtime.
- [ ] Object Pool / `ArrayPool<T>` em buffers de curta duração no hot path.
- [ ] `Span<T>`/`Memory<T>` aplicados em parsing/buffers quando cabível.
- [ ] Thread safety por padrão (sem estado global mutável).

## 6. AOT / Trimming

- [ ] Sem `dynamic`, sem reflection dinâmica.
- [ ] Compila e roda sob `PublishAot=true` (ou marcado explicitamente como incompatível com justificativa).

## 7. Documentação e Decisão

- [ ] Se a decisão for estrutural, um [ADR](adr/README.md) foi criado.
- [ ] Docs relevantes atualizados ([components.md](components.md), [naming-conventions.md](naming-conventions.md), etc.).
- [ ] `Guara.Analyzers` passa sem warnings `GUARA*`.
- [ ] `dotnet build` limpo; testes verdes; benchmark sem regressão acima do baseline.
