using Guara.Abstractions;

namespace Guara.Core;

/// <summary>
/// Compõe os <see cref="IJobMiddleware"/> registrados em um único <see cref="JobDelegate"/>,
/// ordenando pelos <see cref="PipelineSlot"/> canônicos (Validation → … → Notifications).
/// Dentro de um mesmo slot, a ordem de registro é preservada (estável).
/// </summary>
internal sealed class JobPipelineBuilder
{
    private readonly List<Registration> _middlewares = [];
    private int _sequence;

    private readonly record struct Registration(PipelineSlot Slot, int Order, IJobMiddleware Middleware);

    /// <summary>Adiciona um middleware no slot <see cref="PipelineSlot.Custom"/>.</summary>
    /// <param name="middleware">Middleware a adicionar.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public JobPipelineBuilder Use(IJobMiddleware middleware) => Use(PipelineSlot.Custom, middleware);

    /// <summary>Adiciona um middleware num slot específico.</summary>
    /// <param name="slot">Slot canônico do middleware.</param>
    /// <param name="middleware">Middleware a adicionar.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public JobPipelineBuilder Use(PipelineSlot slot, IJobMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _middlewares.Add(new Registration(slot, _sequence++, middleware));
        return this;
    }

    /// <summary>Constrói o delegate encadeado na ordem canônica de slots.</summary>
    /// <param name="terminal">Etapa final opcional, executada após todos os middlewares.</param>
    /// <returns>Um <see cref="JobDelegate"/> que executa o pipeline completo.</returns>
    public JobDelegate Build(JobDelegate? terminal = null)
    {
        var ordered = _middlewares
            .OrderBy(static r => r.Slot)
            .ThenBy(static r => r.Order)
            .Select(static r => r.Middleware)
            .ToArray();

        JobDelegate next = terminal ?? (static (_, _) => ValueTask.CompletedTask);

        for (var i = ordered.Length - 1; i >= 0; i--)
        {
            var middleware = ordered[i];
            var localNext = next;
            next = (ctx, ct) => middleware.InvokeAsync(ctx, localNext, ct);
        }

        return next;
    }
}
