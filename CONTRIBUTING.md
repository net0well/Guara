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
git clone https://github.com/net0well/Guara.git
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
5. **Atualize o README na mesma mudança** (ver abaixo).
6. **Abra o PR** descrevendo o comportamento que muda e como você verificou.

## O README acompanha o código

O `README.md` (e o `README.en.md`) é a primeira coisa que alguém lê sobre o projeto. README
desatualizado não é documentação incompleta: é **documentação errada** — promete pacote que
não existe, marca como planejado o que já está pronto, e faz quem chega perder tempo
procurando algo que nunca foi implementado.

Por isso a atualização vai **na mesma mudança**, nunca "depois". Se o seu PR mexe em algo da
lista abaixo, ele também mexe no README:

| Se você… | Atualize |
|---|---|
| Criou um pacote `Guara.*` | Tabela de pacotes, marcando o estado |
| Concluiu um item do roadmap | Tabela de roadmap, nos dois idiomas |
| Adicionou capacidade visível ao usuário | Tabela de recursos |
| Implementou um provider de storage | Tabela de providers |
| Mudou a API pública mostrada nos exemplos | O trecho de código correspondente |
| Adicionou ação, permissão ou tela do painel | Seção do dashboard |

**Os dois idiomas andam juntos.** Alterar só o português deixa o `README.en.md` mentindo para
quem lê em inglês, e a divergência só cresce.

Marque o que ainda não existe com 🕓 e o que está pronto com ✅. Um recurso descrito sem marca
é lido como disponível hoje.

## O que o CI executa

Todo PR passa por `.github/workflows/ci.yml`, que roda exatamente a mesma sequência que
você roda local: restore, build em Release, testes e `dotnet pack`.

Três coisas que costumam surpreender:

- **O build é o gate de compatibilidade e de AOT.** Warning é erro, e nele rodam os
  analisadores de trimming/AOT e o congelamento da API pública. Não há etapa separada.
- **Node é obrigatório no CI.** A SPA do painel é construída pelo build .NET; sem Node ela
  cairia na página placeholder e ninguém perceberia.
- **`dotnet pack` roda em todo PR.** Erro de empacotamento (metadado ausente, layout de
  analyzer errado) aparece na revisão, não na hora de publicar a tag.

Publicar é sempre por **tag**: `v0.1.0-preview.1` produz pacotes `0.1.0-preview.1`, via
MinVer. Ninguém edita número de versão em arquivo.

A publicação usa **Trusted Publishing**: o GitHub emite um token OIDC assinado, o NuGet.org
confere contra uma policy registrada e devolve uma chave válida por 1 hora. **Não há chave de
API guardada no repositório** — nada a rotacionar, nada a vazar.

Para funcionar, dois lados precisam bater:

- **No nuget.org** (`Trusted Publishing`): dono `net0well`, repositório `Guara`, arquivo de
  workflow `release.yml` (só o nome, sem o caminho) e environment `nuget`.
- **No GitHub** (`Settings > Environments`): um environment chamado `nuget`, com aprovação
  manual obrigatória. É o que separa uma tag de uma publicação irreversível no NuGet.org.

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
