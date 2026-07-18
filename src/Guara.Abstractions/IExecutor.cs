namespace Guara.Abstractions;

/// <summary>
/// Executa um job já adquirido (com posse/lease): roda o pipeline de middlewares,
/// invoca o método do job e persiste o estado final, emitindo
/// <see cref="JobCompleted"/>/<see cref="JobFailed"/>.
/// </summary>
public interface IExecutor
{
    /// <summary>Executa o job identificado por <paramref name="id"/>.</summary>
    /// <param name="id">Id do job (a posse já foi adquirida pelo fluxo de dispatch).</param>
    /// <param name="ct">Token de cancelamento cooperativo (shutdown/perda de posse).</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando a execução termina.</returns>
    ValueTask ExecuteAsync(JobId id, CancellationToken ct);
}

/// <summary>
/// Invoca o método do job a partir do <see cref="IJobContext"/> — <b>sem reflection</b>.
/// A implementação definitiva é gerada em compilação pelo <c>Guara.SourceGenerators</c>
/// até que essa geração exista, o <c>Guara.Executor</c> fornece um registro manual.
/// </summary>
public interface IJobInvoker
{
    /// <summary>Invoca o método do job descrito no contexto.</summary>
    /// <param name="context">Contexto do job em execução.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando o método termina.</returns>
    ValueTask InvokeAsync(IJobContext context, CancellationToken ct);
}
