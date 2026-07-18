namespace Guara.Server;

/// <summary>Opções do servidor.</summary>
public sealed class ServerOptions
{
    /// <summary>Intervalo entre heartbeats do nó.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Sem heartbeat por este período, o nó é considerado morto e removido pela
    /// manutenção (os jobs dele voltam pela expiração de lease).
    /// </summary>
    public TimeSpan ServerTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Intervalo entre ciclos de manutenção (purga por retenção, limpeza de nós).</summary>
    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Por quanto tempo jobs terminados permanecem consultáveis antes da purga.</summary>
    public RetentionPolicy Retention { get; set; } = RetentionPolicy.Default;
}

/// <summary>Política de retenção de jobs em estado terminal.</summary>
/// <param name="Succeeded">Retenção de jobs concluídos com sucesso.</param>
/// <param name="Failed">Retenção de jobs que falharam (maior, para diagnóstico).</param>
public sealed record RetentionPolicy(TimeSpan Succeeded, TimeSpan Failed)
{
    /// <summary>Sucesso por 1 dia; falhas por 7 dias.</summary>
    public static RetentionPolicy Default { get; } = new(TimeSpan.FromDays(1), TimeSpan.FromDays(7));
}
