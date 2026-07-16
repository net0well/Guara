# ADR-0001 — Arquitetura Orientada a Componentes

- **Status:** Aceito
- **Data:** 2026-07-16

## Contexto

Job schedulers tendem a virar monólitos acoplados: o agendador conhece o banco, o worker conhece o dashboard, e trocar de storage exige reescrever o núcleo. Precisamos de um modelo onde cada parte evolua, seja testada e seja publicada de forma independente.

Alternativas consideradas:
- **Camadas tradicionais** (`Controllers → Services → Repositories`): acopla por camada técnica, não por responsabilidade; storage e execução acabam na mesma camada.
- **DDD clássico** (agregados, bounded contexts): peso conceitual desnecessário para um framework de infraestrutura; o "domínio" aqui é técnico.

## Decisão

A **unidade arquitetural é o componente**. Cada componente tem responsabilidade única, ciclo de vida próprio, abstrações, implementações, testes e documentação separados. Inspiração: ASP.NET Core, EF Core, Hangfire, MediatR.

Regras derivadas: um projeto = uma responsabilidade; componentes só se conhecem por interfaces em `Guara.Abstractions`; dependências unidirecionais (`Dashboard → Api → Core → Abstractions`).

## Consequências

**Ganhos:** baixo acoplamento, evolução/versionamento independentes, extensibilidade por composição, testabilidade alta, superfície pública pequena por pacote.

**Custos:** mais projetos na solution; exige disciplina (enforçada por `Guara.Analyzers`); comunicação por contrato/evento em vez de chamada direta adiciona indireção.

Detalhado em [../ARCHITECTURE.md](../ARCHITECTURE.md), [../components.md](../components.md) e [../dependency-rules.md](../dependency-rules.md).
