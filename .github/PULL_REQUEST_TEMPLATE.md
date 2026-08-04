## O que muda

<!-- O comportamento que passa a valer, e por quê. O diff já mostra o quê. -->

## Como verifiquei

<!-- Testes que cobrem, e o que você rodou. Se o provider PostgreSQL foi tocado, diga se
     rodou com Docker no ar. -->

## Checklist

- [ ] `dotnet build Guara.slnx` limpo — o build trata warning como erro e exige doc XML em API pública
- [ ] `dotnet test Guara.slnx` verde
- [ ] Implementação `internal sealed` quando não precisa ser pública
- [ ] Nenhuma referência à classe concreta de outro componente
- [ ] `CancellationToken` recebido e propagado nas APIs assíncronas novas
- [ ] Comentários falam do código — sem citar spec, ADR, critério de aceite ou data
- [ ] `README.md` **e** `README.en.md` atualizados se mudou pacote, roadmap, recurso, provider, API de exemplo ou o painel
- [ ] Contrato de storage alterado? Kit de conformidade atualizado e **todos** os providers passam
- [ ] Decisão estrutural? ADR criado em `docs/adr/`

## Quebra alguma coisa?

<!-- Assinatura pública removida ou alterada, formato persistido, semântica de execução.
     Se sim, descreva a migração. Se não, escreva "não". -->
