using System.Data.Common;
using Guara.Abstractions;
using Guara.Storage;

namespace Guara.Storage.Conformance;

/// <summary>
/// Transação aberta pelo <b>chamador</b>, como o kit de conformidade a manipula: o handle
/// que vai ao Guará, mais o commit e o rollback que só o dono pode dar.
/// <para>
/// É uma abstração, e não a <c>DbTransaction</c> direta, para que um provider não
/// relacional com suporte a transação também consiga se submeter ao kit.
/// </para>
/// </summary>
public abstract class ConformanceTransaction : IAsyncDisposable
{
    /// <summary>O handle entregue ao Guará no enfileiramento.</summary>
    public abstract IGuaraTransaction Handle { get; }

    /// <summary>Confirma a transação — só depois disso o job existe para os demais.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando a transação é confirmada.</returns>
    public abstract ValueTask CommitAsync(CancellationToken ct);

    /// <summary>Desfaz a transação — o job escrito dentro dela nunca existiu.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando a transação é desfeita.</returns>
    public abstract ValueTask RollbackAsync(CancellationToken ct);

    /// <inheritdoc />
    public abstract ValueTask DisposeAsync();
}

/// <summary>
/// Implementação relacional do handle do kit: abre a conexão do provider, inicia a
/// transação nela e a entrega ao Guará. É o que as três fixtures relacionais usam.
/// </summary>
/// <param name="connection">Conexão já aberta, na mesma base que o provider alcança.</param>
/// <param name="transaction">Transação iniciada nessa conexão.</param>
public sealed class RelationalConformanceTransaction(
    DbConnection connection, DbTransaction transaction) : ConformanceTransaction
{
    /// <inheritdoc />
    public override IGuaraTransaction Handle { get; } = new RelationalTransaction(transaction);

    /// <inheritdoc />
    public override async ValueTask CommitAsync(CancellationToken ct)
        => await transaction.CommitAsync(ct);

    /// <inheritdoc />
    public override async ValueTask RollbackAsync(CancellationToken ct)
        => await transaction.RollbackAsync(ct);

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }
}
