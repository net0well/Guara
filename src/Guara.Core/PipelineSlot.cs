namespace Guara.Core;

/// <summary>
/// Slots canônicos do pipeline de execução de jobs, na ordem em que executam.
/// Cada componente contribui com o middleware do seu slot;
/// slots ausentes simplesmente não têm etapa.
/// </summary>
public enum PipelineSlot
{
    /// <summary>Validação do job/args.</summary>
    Validation = 0,

    /// <summary>Autorização da execução.</summary>
    Authorization = 1,

    /// <summary>(De)serialização de argumentos.</summary>
    Serialization = 2,

    /// <summary>Ponto de extensão do usuário (middlewares custom).</summary>
    Custom = 3,

    /// <summary>Métricas.</summary>
    Metrics = 4,

    /// <summary>Logging.</summary>
    Logging = 5,

    /// <summary>Política de retentativa.</summary>
    Retry = 6,

    /// <summary>Invocação do método do job.</summary>
    Execution = 7,

    /// <summary>Marcação de sucesso.</summary>
    Success = 8,

    /// <summary>Notificações pós-execução.</summary>
    Notifications = 9,
}
