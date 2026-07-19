namespace Guara.Server;

/// <summary>Opções do servidor. Configuráveis pela seção <c>Guara:Server</c>.</summary>
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

    /// <summary>Intervalo entre varreduras de recorrentes vencidos (promoção de ocorrências).</summary>
    public TimeSpan RecurringPollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Por quanto tempo jobs terminados permanecem consultáveis antes da purga.</summary>
    public RetentionPolicy Retention { get; set; } = RetentionPolicy.Default;

    internal void Validate()
    {
        if (HeartbeatInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"ServerOptions.HeartbeatInterval precisa ser positivo (recebido: {HeartbeatInterval}).");
        }

        if (ServerTimeout <= HeartbeatInterval)
        {
            throw new InvalidOperationException(
                $"ServerOptions.ServerTimeout ({ServerTimeout}) precisa exceder HeartbeatInterval " +
                $"({HeartbeatInterval}) — senão nós saudáveis seriam removidos entre heartbeats.");
        }

        if (MaintenanceInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"ServerOptions.MaintenanceInterval precisa ser positivo (recebido: {MaintenanceInterval}).");
        }

        if (RecurringPollInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"ServerOptions.RecurringPollInterval precisa ser positivo (recebido: {RecurringPollInterval}).");
        }

        if (Retention is null || Retention.Succeeded < TimeSpan.Zero || Retention.Failed < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "ServerOptions.Retention precisa de períodos não negativos para Succeeded e Failed.");
        }
    }
}

/// <summary>Política de retenção de jobs em estado terminal.</summary>
/// <param name="Succeeded">Retenção de jobs concluídos com sucesso.</param>
/// <param name="Failed">Retenção de jobs que falharam (maior, para diagnóstico).</param>
public sealed record RetentionPolicy(TimeSpan Succeeded, TimeSpan Failed)
{
    /// <summary>Sucesso por 1 dia; falhas por 7 dias.</summary>
    public static RetentionPolicy Default { get; } = new(TimeSpan.FromDays(1), TimeSpan.FromDays(7));
}
