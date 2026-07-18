namespace Guara.Worker;

/// <summary>Opções do worker.</summary>
public sealed class WorkerOptions
{
    /// <summary>Máximo de jobs executando simultaneamente. Default: nº de CPUs.</summary>
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;

    /// <summary>Tempo máximo do drain no shutdown; excedido → cancelamento cooperativo.</summary>
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Intervalo de renovação da posse durante a execução.</summary>
    public TimeSpan LeaseRenewInterval { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Duração da posse a cada renovação (deve exceder o intervalo de renovação).</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);
}
