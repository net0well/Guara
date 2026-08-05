namespace Guara.Abstractions;

/// <summary>
/// Unidade de trabalho <b>do chamador</b> da qual o enfileiramento participa, para que
/// gravar o dado do negócio e enfileirar o job sejam a mesma coisa: ou os dois
/// acontecem, ou nenhum.
/// <para>
/// O Guará nunca confirma nem desfaz esta transação — ele apenas escreve dentro dela. O
/// controle é de quem a abriu, do começo ao fim.
/// </para>
/// <para>
/// O handle é <b>opaco</b> de propósito. Dar-lhe um membro significaria expor tipos de
/// acesso a dados relacionais aqui, numa camada que também serve providers que não são
/// relacionais. Quem o interpreta é o provider, que converte para o tipo que emitiu e
/// recusa o resto com mensagem clara.
/// </para>
/// </summary>
public interface IGuaraTransaction;
