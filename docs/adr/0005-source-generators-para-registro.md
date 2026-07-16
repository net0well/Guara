# ADR-0005 — Source Generators para Descoberta e Registro

- **Status:** Aceito
- **Data:** 2026-07-16

## Contexto

Frameworks costumam descobrir jobs/handlers/middlewares por **reflection** (varredura de assemblies em runtime). Isso aloca, custa no startup, e — decisivo aqui — **quebra Native AOT e Trimming** ([ADR-0008](0008-native-aot-e-trimming.md)), pois o trimmer não enxerga tipos acessados dinamicamente.

## Decisão

Descoberta e registro acontecem em **tempo de compilação** via `Guara.SourceGenerators`. O gerador emite o código de registro (jobs, middlewares, handlers, extensões) — zero reflection em runtime.

`Guara.Analyzers` complementa: acusa uso de reflection dinâmica no runtime e violações das regras de dependência (`GUARA*`).

## Consequências

**Ganhos:** zero reflection em runtime; startup rápido; AOT/Trimming-safe; erros de registro viram erros de compilação, não exceções em produção.

**Custos:** manter geradores é mais complexo que varrer assemblies; tempo de build um pouco maior; contribuidores precisam entender o pipeline incremental do gerador.

Relacionado a [../performance.md](../performance.md) e [../anti-patterns.md](../anti-patterns.md) (item 9).
