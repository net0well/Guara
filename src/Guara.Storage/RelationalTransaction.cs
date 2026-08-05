using System.Data.Common;
using Guara.Abstractions;

namespace Guara.Storage;

/// <summary>
/// Handle de transação para os providers <b>relacionais</b>. Vive aqui, e não em cada
/// provider, porque os bancos relacionais o interpretam da mesma forma: escrever na
/// conexão da transação recebida.
/// <para>
/// O Guará nunca confirma nem desfaz a transação envolvida — quem a abriu continua dono
/// dela.
/// </para>
/// </summary>
/// <param name="transaction">
/// Transação aberta pelo chamador. Precisa estar viva e na conexão que alcança as
/// tabelas do Guará, o que significa Guará e aplicação no mesmo banco.
/// </param>
public sealed class RelationalTransaction(DbTransaction transaction) : IGuaraTransaction
{
    /// <summary>A transação do chamador em que o provider vai escrever.</summary>
    public DbTransaction Transaction { get; } = transaction
        ?? throw new ArgumentNullException(nameof(transaction));

    /// <summary>
    /// Converte um handle genérico para o desta família, recusando o que veio de outra.
    /// Misturar famílias é erro de composição — melhor falhar dizendo quem recebeu o quê
    /// do que escrever fora da transação que o chamador acha que abriu.
    /// </summary>
    /// <param name="transaction">Handle recebido do chamador.</param>
    /// <param name="provider">Nome do provider, para a mensagem de erro.</param>
    /// <returns>O handle relacional.</returns>
    /// <exception cref="NotSupportedException">Quando o handle é de outra família.</exception>
    public static RelationalTransaction Require(IGuaraTransaction transaction, string provider)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        return transaction as RelationalTransaction
               ?? throw new NotSupportedException(
                   $"O provider {provider} espera uma transação relacional " +
                   $"({nameof(RelationalTransaction)}), mas recebeu {transaction.GetType().Name}. " +
                   "Construa o handle a partir da DbTransaction aberta na mesma conexão que " +
                   "alcança as tabelas do Guará.");
    }

    /// <summary>
    /// A transação já no tipo do provider. Recusar aqui um handle de <b>outro banco</b>
    /// troca um <c>InvalidCastException</c> no meio do comando por uma mensagem que diz
    /// exatamente o que foi misturado.
    /// </summary>
    /// <typeparam name="TTransaction">Tipo de transação do provider.</typeparam>
    /// <param name="provider">Nome do provider, para a mensagem de erro.</param>
    /// <returns>A transação do chamador, tipada.</returns>
    /// <exception cref="NotSupportedException">Quando a transação é de outro banco.</exception>
    public TTransaction RequireTransaction<TTransaction>(string provider)
        where TTransaction : DbTransaction
        => Transaction as TTransaction
           ?? throw new NotSupportedException(
               $"A transação informada é {Transaction.GetType().Name}, de outro banco. " +
               $"O storage {provider} só participa de uma transação aberta na conexão dele.");

    /// <summary>
    /// A conexão da transação, já tipada e validada. Falhar aqui, e não no meio do
    /// comando, deixa claro que o problema é a transação recebida — não a escrita do job.
    /// </summary>
    /// <typeparam name="TConnection">Tipo de conexão do provider.</typeparam>
    /// <param name="provider">Nome do provider, para a mensagem de erro.</param>
    /// <returns>A conexão em que o comando deve rodar.</returns>
    /// <exception cref="InvalidOperationException">Quando a transação não tem mais conexão.</exception>
    /// <exception cref="NotSupportedException">Quando a conexão é de outro banco.</exception>
    public TConnection RequireConnection<TConnection>(string provider)
        where TConnection : DbConnection
    {
        var connection = Transaction.Connection
            ?? throw new InvalidOperationException(
                "A transação informada não tem mais conexão associada — ela já foi confirmada, " +
                "desfeita ou descartada. Enfileire antes de encerrar a transação.");

        return connection as TConnection
               ?? throw new NotSupportedException(
                   $"A conexão da transação é {connection.GetType().Name}, de outro banco. " +
                   $"O storage {provider} só participa de uma transação aberta na conexão dele.");
    }
}
