# ADR-0006 — Um `AddGuara...()` por Pacote

- **Status:** Aceito
- **Data:** 2026-07-16

## Contexto

APIs públicas grandes envelhecem mal: cada método vira contrato a manter. Queremos uma superfície mínima, previsível e alinhada ao ecossistema .NET (`builder.Services.Add...`).

## Decisão

Cada pacote expõe **exatamente uma** extensão de entrada, no namespace `Microsoft.Extensions.DependencyInjection`:

- `AddGuara...()` para ligar um componente/capacidade.
- `Use...()` para selecionar uma implementação de um ponto de extensão (provider).

A extensão recebe e devolve `IGuaraBuilder`, habilitando composição fluente. Todo registro de DI do componente vive **dentro** desse método — nunca espalhado pela aplicação.

```csharp
builder.Services
    .AddGuara()
    .UseSqlServerStorage(conn)
    .AddGuaraServer()
    .AddGuaraDashboard();
```

## Consequências

**Ganhos:** superfície pública minúscula e descobrível via IntelliSense; sem `using` extra; impossível "registrar errado" fora do método; fácil de versionar.

**Custos:** se um pacote parece precisar de dois pontos de entrada, é sinal de que são dois componentes — obriga a repensar a fronteira (o que é desejável, mas custa refatoração).

Detalhado em [../naming-conventions.md](../naming-conventions.md) e [../patterns.md](../patterns.md).
