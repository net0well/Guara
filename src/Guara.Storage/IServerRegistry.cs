namespace Guara.Storage;

/// <summary>
/// Registro de nós servidores: anúncio, heartbeat e remoção de nós mortos.
/// Alimenta o dashboard (quem está vivo) e a manutenção (limpeza de nós expirados).
/// </summary>
public interface IServerRegistry
{
    /// <summary>Anuncia (ou reanuncia) um nó. Upsert pelo <see cref="ServerNode.Id"/>.</summary>
    /// <param name="node">Identidade completa do nó.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando o nó está registrado.</returns>
    ValueTask AnnounceAsync(ServerNode node, CancellationToken ct);

    /// <summary>
    /// Registra um heartbeat. Retorna <c>false</c> quando o nó não existe mais
    /// (removido pela manutenção) — o chamador deve reanunciar-se.
    /// </summary>
    /// <param name="serverId">Id do nó.</param>
    /// <param name="now">Instante do heartbeat (relógio do chamador).</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> se o heartbeat foi registrado.</returns>
    ValueTask<bool> HeartbeatAsync(string serverId, DateTimeOffset now, CancellationToken ct);

    /// <summary>Remove o registro de um nó (desligamento gracioso).</summary>
    /// <param name="serverId">Id do nó.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando o registro foi removido.</returns>
    ValueTask RemoveAsync(string serverId, CancellationToken ct);

    /// <summary>Lista os nós registrados.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Os nós conhecidos, com seu último heartbeat.</returns>
    ValueTask<IReadOnlyList<ServerNode>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Remove nós cujo último heartbeat é anterior a <paramref name="heartbeatBefore"/>
    /// (nós mortos). Os jobs que eles seguravam voltam a ser elegíveis pela expiração de lease.
    /// </summary>
    /// <param name="heartbeatBefore">Instante de corte.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Quantidade de nós removidos.</returns>
    ValueTask<int> RemoveExpiredAsync(DateTimeOffset heartbeatBefore, CancellationToken ct);
}
