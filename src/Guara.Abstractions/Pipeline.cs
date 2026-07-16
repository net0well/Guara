using System.Security.Claims;

namespace Guara.Abstractions;

/// <summary>Contexto que trafega pelo pipeline de execução de um job.</summary>
public interface IJobContext
{
    /// <summary>Identificador do job.</summary>
    JobId Id { get; }

    /// <summary>Descrição do job.</summary>
    JobDescriptor Descriptor { get; }

    /// <summary>Estado atual do job.</summary>
    JobState State { get; }

    /// <summary>Número da tentativa atual (0 = primeira).</summary>
    int Attempt { get; }

    /// <summary>Saco de propriedades para middlewares customizados.</summary>
    IDictionary<string, object?> Items { get; }

    /// <summary>Principal associado ao job, quando houver autorização.</summary>
    ClaimsPrincipal? User { get; }
}

/// <summary>Executa o restante do pipeline.</summary>
/// <param name="context">Contexto do job.</param>
/// <param name="ct">Token de cancelamento.</param>
/// <returns>Uma <see cref="ValueTask"/> que conclui quando o restante do pipeline termina.</returns>
public delegate ValueTask JobDelegate(IJobContext context, CancellationToken ct);

/// <summary>Uma etapa do pipeline de execução de jobs (modelo ASP.NET Core).</summary>
public interface IJobMiddleware
{
    /// <summary>Executa a etapa e, se apropriado, chama <paramref name="next"/>.</summary>
    /// <param name="context">Contexto do job.</param>
    /// <param name="next">Próxima etapa do pipeline.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Uma <see cref="ValueTask"/> que conclui quando a etapa termina.</returns>
    ValueTask InvokeAsync(IJobContext context, JobDelegate next, CancellationToken ct);
}
