namespace Guara.Dispatcher;

/// <summary>Opções do dispatcher. Configuráveis pela seção <c>Guara:Dispatcher</c>.</summary>
public sealed class DispatcherOptions
{
    /// <summary>
    /// Teto da espera quando não há jobs elegíveis. O laço acorda antes disso ao receber
    /// um aviso de trabalho novo; o intervalo é a garantia para o que se torna elegível
    /// sem aviso (retentativa vencida, lease abandonado).
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Filas consumidas, em ordem de prioridade.</summary>
    public string[] Queues { get; set; } = ["default"];

    /// <summary>Duração da posse na aquisição (renovada pelo worker durante a execução).</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Teto do back-off exponencial quando o storage está indisponível.</summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(1);

    internal void Validate()
    {
        if (PollingInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"DispatcherOptions.PollingInterval precisa ser positivo (recebido: {PollingInterval}).");
        }

        if (Queues is not { Length: > 0 } || Queues.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                "DispatcherOptions.Queues precisa de pelo menos uma fila com nome não vazio.");
        }

        if (LeaseDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"DispatcherOptions.LeaseDuration precisa ser positivo (recebido: {LeaseDuration}).");
        }

        if (MaxBackoff < PollingInterval)
        {
            throw new InvalidOperationException(
                $"DispatcherOptions.MaxBackoff ({MaxBackoff}) não pode ser menor que PollingInterval ({PollingInterval}).");
        }
    }
}
