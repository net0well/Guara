using Guara.Abstractions;

namespace Guara.Core;

/// <summary>
/// Middleware genérico de retentativa (slot <see cref="PipelineSlot.Retry"/>): reexecuta o
/// restante do pipeline em caso de exceção, respeitando <see cref="RetryOptions.MaxAttempts"/>
/// e o back-off. Usa <see cref="TimeProvider"/> para os atrasos (testável).
/// </summary>
public sealed class RetryMiddleware(RetryOptions options, TimeProvider? timeProvider = null) : IJobMiddleware
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
            catch (Exception) when (attempt < options.MaxAttempts && !ct.IsCancellationRequested)
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
