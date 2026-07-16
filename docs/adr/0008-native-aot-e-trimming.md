# ADR-0008 — Compatibilidade com Native AOT e Trimming

- **Status:** Aceito
- **Data:** 2026-07-16

## Contexto

Cargas de trabalho modernas (containers, serverless, edge) valorizam startup rápido e binários pequenos. Native AOT e Trimming entregam isso, mas impõem restrições: nada de reflection dinâmica, `dynamic` ou geração de código em runtime.

## Decisão

Os pacotes de runtime do Guará são **AOT-safe e Trimming-safe** por padrão. Consequências de design:

- Descoberta/registro por Source Generators, não reflection ([ADR-0005](0005-source-generators-para-registro.md)).
- Serialização com geradores (ex.: `System.Text.Json` source-gen), não reflection.
- Sem `dynamic`; anotações de trimming (`DynamicallyAccessedMembers`, `RequiresUnreferencedCode`) onde inevitável.
- Matriz de CI inclui build `PublishAot=true` para os pacotes runtime.

Pacotes fora do caminho de runtime (ex.: `Guara.Cli`, ferramentas) podem relaxar essa regra com justificativa explícita.

## Consequências

**Ganhos:** startup e footprint reduzidos; erros de trimming detectados no CI, não em produção; força um design sem reflection, que também é mais rápido.

**Custos:** algumas bibliotecas de terceiros não são AOT-safe e ficam barradas ou isoladas; contribuidores precisam conhecer as regras de AOT/Trimming.

Relacionado a [../performance.md](../performance.md).
