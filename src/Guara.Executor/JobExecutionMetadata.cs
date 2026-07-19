using Guara.Abstractions;

namespace Guara.Executor;

/// <summary>
/// Comportamento declarado nos atributos do job, materializado em compilação pelo
/// source generator (ou informado no registro manual) — nenhum atributo é lido por
/// reflection em runtime.
/// </summary>
public sealed record JobExecutionMetadata
{
    /// <summary>Fila do job (<c>[GuaraFila]</c>); <c>null</c> usa o default.</summary>
    public string? Queue { get; init; }

    /// <summary>Retentativas máximas (<c>[GuaraRetentativas]</c>); <c>null</c> usa a política global.</summary>
    public int? MaxAttempts { get; init; }

    /// <summary>Tempo máximo de execução em segundos (<c>[GuaraTempoLimite]</c>).</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Recorrente pula a ocorrência se a anterior ainda roda (<c>[GuaraPularSeAnteriorEmExecucao]</c>).</summary>
    public bool SkipIfPreviousRunning { get; init; }

    /// <summary>
    /// Chave do mutex de concorrência (<c>[GuaraDesabilitarConcorrencia]</c>), resolvida
    /// por execução — o generator emite a formatação dos placeholders com os argumentos
    /// desserializados. <c>null</c> = job sem exclusão mútua.
    /// </summary>
    public Func<IJobContext, string>? ConcurrencyKey { get; init; }

    /// <summary>Quanto aguardar pela chave antes de devolver o job à fila (0 = imediato).</summary>
    public int ConcurrencyWaitSeconds { get; init; }
}

/// <summary>Consulta de metadados de execução por job (implementada pelo registro).</summary>
public interface IJobMetadataProvider
{
    /// <summary>Metadados do job, ou <c>null</c> quando não declarados.</summary>
    /// <param name="typeName">Nome do tipo do job.</param>
    /// <param name="methodName">Nome do método do job.</param>
    /// <returns>Os metadados registrados, ou <c>null</c>.</returns>
    JobExecutionMetadata? GetMetadata(string typeName, string methodName);
}

/// <summary>
/// Módulo de registro de jobs — implementado pelo código gerado (um por assembly com
/// <c>[GuaraJob]</c>); aplicado na materialização do <see cref="JobHandlerRegistry"/>.
/// </summary>
public interface IJobModule
{
    /// <summary>Registra os handlers e metadados do módulo.</summary>
    /// <param name="registry">Registro de destino.</param>
    void Register(JobHandlerRegistry registry);
}
