namespace Guara.Core;

/// <summary>Opções do <see cref="RetryMiddleware"/>.</summary>
public sealed class RetryOptions
{
    /// <summary>
    /// Número máximo de retentativas após a primeira falha. <c>3</c> por padrão
    /// (jobs com efeito colateral irreversível devem usar <c>0</c>).
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Back-off para a retentativa de índice <c>attempt</c> (0 = primeira retentativa).
    /// Padrão: exponencial 2^attempt segundos.
    /// </summary>
    public Func<int, TimeSpan> Backoff { get; set; } =
        static attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt));
}
