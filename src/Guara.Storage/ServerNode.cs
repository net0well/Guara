namespace Guara.Storage;

/// <summary>
/// Um nó servidor registrado no storage. O heartbeat periódico mantém
/// <see cref="LastHeartbeat"/> atualizado; nós sem heartbeat recente são
/// removidos pela manutenção e seus jobs recuperados pela expiração de lease.
/// </summary>
public sealed record ServerNode
{
    /// <summary>Identificador único do nó (máquina + processo + sufixo aleatório).</summary>
    public required string Id { get; init; }

    /// <summary>Nome da máquina que hospeda o nó.</summary>
    public required string MachineName { get; init; }

    /// <summary>Instante em que o nó iniciou (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Último heartbeat registrado (UTC).</summary>
    public required DateTimeOffset LastHeartbeat { get; init; }

    /// <summary>Filas que o nó consome, em ordem de prioridade.</summary>
    public string[] Queues { get; init; } = [];

    /// <summary>Máximo de jobs executando simultaneamente no nó.</summary>
    public int MaxConcurrency { get; init; }

    /// <summary>
    /// Papéis coordenados que este nó lidera no momento (ver <c>ClusterRoles</c>). Vazio
    /// quando o nó só executa jobs — que é o caso de todos menos um, por papel.
    /// </summary>
    public string[] Roles { get; init; } = [];
}
