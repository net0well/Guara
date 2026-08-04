# Política de segurança

## Como reportar uma vulnerabilidade

**Não abra issue pública para vulnerabilidade.** Uma issue aberta expõe a falha antes de
existir correção, e o Guará executa código de terceiros num processo de servidor — o risco
de exploração até a versão corrigida sair é real.

Use o canal privado do GitHub:

**https://github.com/net0well/guara/security/advisories/new**

Ajuda muito ter no relatório:

- Versão do Guará, TFM (`net8.0`/`net10.0`) e provider de storage em uso.
- O que um atacante consegue fazer, e a partir de que posição (anônimo na rede, usuário
  autenticado do painel, autor de um job).
- Passos ou projeto mínimo que reproduzem.

## Prazos

| Etapa | Prazo |
|---|---|
| Confirmação de recebimento | 5 dias corridos |
| Avaliação inicial com severidade | 10 dias corridos |
| Correção publicada ou plano com data | 90 dias corridos |

Divulgação coordenada: o aviso público sai junto da versão corrigida, com crédito a quem
reportou — a menos que você prefira permanecer anônimo.

## Versões suportadas

Enquanto o projeto está pré-1.0, apenas a última versão publicada recebe correção. A partir
do 1.0 esta tabela passa a listar as linhas suportadas.

## Escopo

Entra no escopo qualquer coisa que quebre as garantias do framework, por exemplo:

- Acesso ao painel ou às suas ações sem a permissão correspondente.
- Execução de job fora do contrato (payload que escapa da desserialização esperada).
- Injeção de SQL nos providers de storage.
- Vazamento de credencial de conexão em log, resposta HTTP ou mensagem de erro.
- Quebra da exclusão mútua que faça um job rodar em dobro sob concorrência.

**Não** entram: falhas de configuração da aplicação que usa o Guará (rodar o painel sem
autorização, expor o processo na internet aberta), nem dependências de terceiros —
reporte-as ao projeto de origem.
