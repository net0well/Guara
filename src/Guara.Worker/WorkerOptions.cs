namespace Guara.Worker;

/// <summary>Opções do worker. Configuráveis pela seção <c>Guara:Worker</c>.</summary>
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

    internal void Validate()
    {
        if (MaxConcurrency < 1)
        {
            throw new InvalidOperationException(
                $"WorkerOptions.MaxConcurrency precisa ser >= 1 (recebido: {MaxConcurrency}).");
        }

        if (ShutdownDrainTimeout < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"WorkerOptions.ShutdownDrainTimeout não pode ser negativo (recebido: {ShutdownDrainTimeout}).");
        }

        if (LeaseRenewInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"WorkerOptions.LeaseRenewInterval precisa ser positivo (recebido: {LeaseRenewInterval}).");
        }

        if (LeaseDuration <= LeaseRenewInterval)
        {
            throw new InvalidOperationException(
                $"WorkerOptions.LeaseDuration ({LeaseDuration}) precisa exceder LeaseRenewInterval " +
                $"({LeaseRenewInterval}) — senão a posse expira antes da renovação.");
        }
    }
}
