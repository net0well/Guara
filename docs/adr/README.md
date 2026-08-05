# Architecture Decision Records (ADRs)

Registro versionado das decisões arquiteturais do Guará. **Nenhuma decisão estrutural entra no código sem um ADR.**

## Formato

Cada ADR usa o template mínimo:

```markdown
# ADR-NNNN — Título

- **Status:** Proposto | Aceito | Substituído por ADR-XXXX | Depreciado
- **Data:** AAAA-MM-DD
- **Contexto:** por que a decisão é necessária
- **Decisão:** o que foi decidido
- **Consequências:** trade-offs, ganhos e custos
```

## Ciclo de vida

`Proposto` → `Aceito` → (`Substituído`/`Depreciado`). ADRs não são editados após aceitos; para mudar uma decisão, crie um novo ADR que substitui o anterior.

## Índice

| ADR | Título | Status |
|---|---|---|
| [0001](0001-arquitetura-orientada-a-componentes.md) | Arquitetura orientada a componentes | Aceito |
| [0002](0002-comunicacao-por-eventos.md) | Comunicação por eventos entre componentes | Aceito |
| [0003](0003-abstracao-de-storage-por-provider.md) | Abstração de Storage por provider | Aceito |
| [0004](0004-channel-para-filas-internas.md) | `Channel<T>` para filas internas | Aceito |
| [0005](0005-source-generators-para-registro.md) | Source Generators para descoberta/registro | Aceito |
| [0006](0006-uma-extensao-addguara-por-pacote.md) | Um `AddGuara...()` por pacote | Aceito |
| [0007](0007-pipeline-de-middlewares.md) | Pipeline de middlewares para execução | Aceito |
| [0008](0008-native-aot-e-trimming.md) | Compatibilidade com Native AOT e Trimming | Aceito |
| [0009](0009-politica-de-dependencias.md) | Política de dependências (núcleo sem terceiros; drivers isolados; cron próprio) | Aceito |
| [0010](0010-api-do-usuario-em-portugues.md) | API voltada ao usuário em português (métodos do `IGuaraClient`) | Aceito |
| [0011](0011-licenca-apache-e-assinatura-de-assembly.md) | Core sob Apache-2.0 e assemblies com nome forte | Aceito |
| [0012](0012-wakeup-por-sinal-de-fila.md) | Wakeup por sinal de fila (`IQueueSignal`), com o polling como piso | Aceito |
| [0013](0013-redis-como-acelerador.md) | Redis como acelerador (`Guara.Redis`), não como storage — emenda o 0009 | Aceito |
| [0014](0014-enfileiramento-transacional.md) | Enfileiramento dentro da transação do chamador; `BeginTransactionAsync` removido | Aceito |
| [0015](0015-elegibilidade-como-instante-indexavel.md) | Elegibilidade materializada em `eligible_at`; ordem de início passa a ser por elegibilidade | Aceito |

## Próximo número

O próximo ADR é **0016**. Numeração sequencial, sem lacunas.
