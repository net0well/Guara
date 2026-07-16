using Guara.Abstractions;

namespace Guara.Core;

/// <summary>
/// Compõe os <see cref="IJobMiddleware"/> registrados em um único <see cref="JobDelegate"/>.
/// Os middlewares executam na ordem de registro; a ordenação canônica por slots
/// (Validation → … → Notifications, spec 002) é aplicada quando os componentes existirem.
/// </summary>
public sealed class JobPipelineBuilder
{
    private readonly List<IJobMiddleware> _middlewares = [];

    /// <summary>Adiciona um middleware ao pipeline.</summary>
    /// <param name="middleware">Middleware a adicionar.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public JobPipelineBuilder Use(IJobMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _middlewares.Add(middleware);
        return this;
    }

    /// <summary>Constrói o delegate encadeado.</summary>
    /// <param name="terminal">Etapa final opcional executada após todos os middlewares.</param>
    /// <returns>Um <see cref="JobDelegate"/> que executa o pipeline completo.</returns>
    public JobDelegate Build(JobDelegate? terminal = null)
    {
        JobDelegate next = terminal ?? (static (_, _) => ValueTask.CompletedTask);

        for (var i = _middlewares.Count - 1; i >= 0; i--)
        {
            var middleware = _middlewares[i];
            var localNext = next;
            next = (ctx, ct) => middleware.InvokeAsync(ctx, localNext, ct);
        }

        return next;
    }
}
