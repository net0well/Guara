# Contribuindo com o Guará

Obrigado pelo interesse. Este documento diz como rodar o projeto, o que a arquitetura exige
e como uma mudança chega ao `main`.

Idioma: documentação e discussão em **português**. Nomes de tipo, DI, options e rotas ficam
em **inglês**; a API voltada ao usuário (`IGuaraClient`, atributos de job) é em português por
decisão registrada em [ADR-0010](docs/adr/0010-api-do-usuario-em-portugues.md).

## Rodando localmente

Requisitos: **.NET SDK 10** (fixado em `global.json`), **Node 20.19+** para a SPA e **Docker**
para os testes de conformidade do PostgreSQL.

```bash
git clone https://github.com/net0well/guara.git
cd guara
dotnet build Guara.slnx
dotnet test Guara.slnx
```

O app de exemplo sobe o servidor e o painel num processo só:

```bash
dotnet run --project samples/Guara.Host
# painel em http://localhost:5080/guara — a senha sai no log do boot
```

Cobertura de execução:

```bash
dotnet test Guara.slnx --collect:"XPlat Code Coverage" --settings coverage.runsettings
```

Sem Docker no ar, `Guara.Storage.PostgreSql.Tests` falha por ambiente — não é defeito de
código, mas o provider fica sem verificação.

## A arquitetura é lei

Antes de escrever código, leia [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md). O Guará é
orientado a **componentes**, não a camadas nem a DDD clássico, e as três leis não são
negociáveis:

1. **Um projeto, uma responsabilidade.**
2. **Dependências unidirecionais:** `Dashboard → Api → Core → Abstractions`. `Abstractions`
   não depende de nada.
3. **Comunicação só por contrato ou evento** — nunca a classe concreta de outro componente.

Consequências práticas ao abrir um PR:

- Implementação nasce `internal sealed`; o mundo externo depende do contrato.
- Um único `AddGuara...()` por pacote, no namespace `Microsoft.Extensions.DependencyInjection`.
- Zero reflection em runtime (use os source generators), `ValueTask` em caminho crítico,
  `CancellationToken` recebido e propagado, AOT/trimming-safe.
- Sem factory estática global, sem `.Result`, `.Wait()` ou `Thread.Sleep`.

A lista completa do que **não** fazer está em [`docs/anti-patterns.md`](docs/anti-patterns.md).

## Fluxo de uma mudança

1. **Abra uma issue antes** para qualquer coisa maior que correção pontual. Componente novo
   ou mudança de contrato começa por uma spec aprovada em [`spec/`](spec/README.md) — o
   código não parte de rascunho.
2. **Branch a partir do `main`**, com nome que descreva a entrega:
   `feat/busca-no-storage`, `fix/lease-expirado`, `docs/guia-postgres`.
3. **Commits semânticos**, com o corpo explicando **por que** — o diff já mostra o quê:

   ```
   feat(storage): busca composta e serie temporal de desfechos

   JobQuery so filtrava estado e fila, e o painel nao tinha como perguntar
   quantas paginas existem nem de onde tirar um grafico.
   ```

   Prefixos: `feat`, `fix`, `refactor`, `perf`, `test`, `docs`, `build`, `chore`.
4. **Rode o [checklist](docs/checklist.md)** e garanta `dotnet build` e `dotnet test` verdes.
   O build trata **warning como erro** e exige doc XML em toda API pública: um símbolo
   público sem `<summary>` quebra a compilação.
5. **Abra o PR** descrevendo o comportamento que muda e como você verificou.

## Comentários de código

Comentários descrevem **o código** — o quê e o porquê técnico. Nunca referenciam specs, ADRs,
números de critério de aceite, datas ou caminhos de documento: isso vive nos `.md` e envelhece
lá, não no meio da lógica.

```csharp
// ❌ posse perdida; aborta a execução local (spec 007, AC-7)
// ✅ posse perdida: aborta a execução local para nunca processar o job em dobro
```

## Testes

- `dotnet-claude-kit`/xUnit v3, um projeto de teste por componente.
- Todo provider de storage herda `tests/Guara.Storage.Conformance` e precisa passar 100%.
- Teste comportamento, não assinatura: a suíte é orientada ao que o componente faz.
- Nada de `Thread.Sleep` para sincronizar — use `TimeProvider` injetado.

## Licença da sua contribuição

O core é [Apache-2.0](LICENSE). Ao abrir um PR você concorda em licenciar sua contribuição
sob os mesmos termos. Contribuições que afetem os pacotes comerciais `Guara.Pro.*` exigirão
CLA — hoje esses pacotes não aceitam contribuição externa.

## Segurança

Vulnerabilidade **não** vira issue pública. Veja [SECURITY.md](SECURITY.md).
