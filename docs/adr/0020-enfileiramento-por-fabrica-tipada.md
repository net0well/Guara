# ADR-0020 — Enfileiramento por Fábrica Tipada, Não por Lambda

- **Status:** Aceito
- **Data:** 2026-08-06

## Contexto

As specs 019, 029, 030 e 038 descrevem a API de enfileiramento na forma que Hangfire e Quartz popularizaram:

```csharp
await jobs.EnfileirarAsync(() => servico.GerarRelatorioAsync(clienteId), ct);
```

Essa forma **nunca foi construída**. O que existe é a fábrica que o source generator emite a partir de `[GuaraJob]`:

```csharp
await jobs.EnfileirarAsync(RelatorioServiceGuara.GerarRelatorio(clienteId), ct);
```

Os documentos ficaram descrevendo a intenção original, e os READMEs chegaram a mostrar a forma inexistente em quatro trechos por idioma — publicados desde o `preview.1`, corrigidos depois. Falta registrar a decisão em si, para que ela pare de reaparecer.

## Decisão

`IGuaraClient` e `IBatchClient` recebem `JobDescriptor`. A forma de produzir um descritor é a fábrica gerada. Não haverá sobrecarga que aceite lambda.

### Por que a lambda é incompatível com o que o projeto promete

Capturar `() => servico.Metodo(x)` como código executável exige `Expression<T>`, e uma `Expression` só vira chamada por `Compile()` — emissão de IL em runtime — ou por caminhada da árvore com `MethodInfo.Invoke`. **As duas são reflection em runtime**, que o [ADR-0008](0008-native-aot-e-trimming.md) exclui e que quebra publicação Native AOT.

Existe um caminho que preservaria a sintaxe: **interceptors** de C#, que substituiriam a chamada em compilação. Mas interceptors ainda são recurso em evolução, e apoiar a API pública central do framework — a linha que todo usuário escreve — sobre um recurso instável é risco desproporcional ao ganho.

### O que se perde, honestamente

A lambda dá **navegação e renomeação diretas**: `F12` sobre o método, e o rename do IDE atravessa a chamada.

Com a fábrica, isso é indireto: navega-se para `RelatorioServiceGuara.GerarRelatorio`, que é código gerado, e de lá para o método real. O rename do método propaga (o gerador reemite), mas a leitura tem um salto a mais.

### O que se ganha

**Erro em compilação, não em runtime.** A lambda aceita qualquer expressão e só descobre no worker que o argumento não é serializável, que o tipo é genérico ou que a assinatura não é suportada. A fábrica só existe para o que é válido: argumento não serializável vira `GUARA0102`, job genérico vira `GUARA0105`, retorno não suportado vira `GUARA0106` — todos na build.

E a garantia que o projeto vende: **zero reflection em runtime, AOT-safe**, sem asterisco.

## Consequências

**Ganhos:** a promessa de AOT vale sem exceção; a classe inteira de erro "job quebra ao executar por causa da assinatura" deixa de existir; e a API pública para de depender de um recurso de linguagem em evolução.

**Custos:** quem vem do Hangfire não encontra a sintaxe que conhece, e precisa do atributo `[GuaraJob]` no método antes de poder enfileirá-lo. O guia de migração precisa tratar isso como a primeira diferença, não como nota de rodapé.

**Efeito nos documentos:** as specs 019, 029, 030 e 038 têm critérios de aceitação escritos sobre a forma de lambda. Ficam marcados como substituídos por este ADR em vez de reescritos — o que foi decidido antes continua legível, e o que vale hoje está aqui.
