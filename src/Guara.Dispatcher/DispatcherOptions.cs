namespace Guara.Dispatcher;

/// <summary>Opções do dispatcher (spec 006).</summary>
public sealed class DispatcherOptions
{
    /// <summary>Intervalo de polling quando não há jobs elegíveis.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Filas consumidas, em ordem de prioridade.</summary>
    public string[] Queues { get; set; } = ["default"];

    /// <summary>Duração da posse na aquisição (renovada pelo worker durante a execução).</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Teto do back-off exponencial quando o storage está indisponível.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(1);
}
