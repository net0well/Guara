using Guara.Abstractions;

namespace Guara.Core;

/// <summary>
/// Retentativa <b>em processo</b>: reexecuta o restante do pipeline na hora, sem tocar o
/// storage, até <see cref="RetryOptions.InProcessAttempts"/> vezes. Só é registrado quando
/// essa opção é maior que zero.
/// <para>
/// Conta com <see cref="RetryOptions.InProcessAttempts"/>, e <b>não</b> com
/// <see cref="RetryOptions.MaxAttempts"/>: os dois modos se compõem — o que sobrevive a
/// este middleware vira uma tentativa persistente —, então usar o mesmo número nos dois
/// multiplicaria as execuções em vez de somá-las.
/// </para>
/// <para>
/// Não repete em cancelamento: shutdown, tempo limite estourado e perda da chave de
/// exclusão mútua cancelam o token, e insistir ali seria seguir trabalhando depois de
/// deixar de ter direito a isso.
/// </para>
/// </summary>
internal sealed class RetryMiddleware(RetryOptions options, TimeProvider? timeProvider = null) : IJobMiddleware
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public async ValueTask InvokeAsync(IJobContext context, JobDelegate next, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await next(context, ct);
                return;
            }
            catch (Exception) when (attempt < options.InProcessAttempts && !ct.IsCancellationRequested)
            {
                if (context is JobContext jobContext)
                {
                    jobContext.IncrementAttempt();
                }

                var delay = options.Backoff(attempt);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, _time, ct);
                }
            }
        }
    }
}
