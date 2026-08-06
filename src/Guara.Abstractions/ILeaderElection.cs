namespace Guara.Abstractions;

/// <summary>
/// Coordenação entre nós: garante que apenas um nó execute um papel por vez.
/// <para>
/// Existe para o trabalho que <b>não</b> pode ser dividido entre nós — promover
/// recorrentes, rodar manutenção —, ao contrário da execução de jobs, que é distribuída
/// de propósito e coordenada por posse individual.
/// </para>
/// </summary>
public interface ILeaderElection
{
    /// <summary>
    /// Tenta assumir a liderança de um papel. Não bloqueia esperando: quem não conseguiu
    /// tenta de novo no próximo ciclo, porque o líder atual pode estar no meio de um
    /// trabalho longo e legítimo.
    /// </summary>
    /// <param name="role">Nome do papel; nós que disputam o mesmo trabalho usam o mesmo nome.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>A liderança assumida, ou <c>null</c> quando outro nó já a detém.</returns>
    ValueTask<ILeadership?> TryAcquireAsync(string role, CancellationToken ct);
}

/// <summary>
/// Liderança em curso. Descartar devolve o papel para disputa.
/// <para>
/// A posse é renovada em segundo plano enquanto este objeto viver. Se a renovação
/// falhar — nó particionado, banco indisponível, pausa longa —, <see cref="Lost"/> é
/// cancelado: quem lidera <b>precisa</b> propagar esse token ao trabalho, para parar no
/// instante em que deixa de ser líder em vez de seguir agindo como se fosse.
/// </para>
/// </summary>
public interface ILeadership : IAsyncDisposable
{
    /// <summary>Papel liderado.</summary>
    string Role { get; }

    /// <summary>
    /// Cancelado quando a liderança é perdida. É o mesmo contrato que a posse de job
    /// oferece ao worker: descobriu que não é mais dono, para de agir como dono.
    /// </summary>
    CancellationToken Lost { get; }
}
