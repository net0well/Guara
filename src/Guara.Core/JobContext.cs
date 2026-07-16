using System.Security.Claims;
using Guara.Abstractions;

namespace Guara.Core;

/// <summary>
/// Implementação concreta de <see cref="IJobContext"/>, reutilizável (pooling):
/// use <see cref="Initialize"/> ao adquirir e <see cref="Reset"/> ao devolver ao pool.
/// </summary>
public sealed class JobContext : IJobContext
{
    private Dictionary<string, object?>? _items;

    /// <inheritdoc />
    public JobId Id { get; private set; }

    /// <inheritdoc />
    public JobDescriptor Descriptor { get; private set; } = default!;

    /// <inheritdoc />
    public JobState State { get; set; }

    /// <inheritdoc />
    public int Attempt { get; private set; }

    /// <inheritdoc />
    public ClaimsPrincipal? User { get; set; }

    /// <inheritdoc />
    public IDictionary<string, object?> Items => _items ??= new Dictionary<string, object?>();

    /// <summary>Prepara o contexto para executar um job.</summary>
    /// <param name="id">Identificador do job.</param>
    /// <param name="descriptor">Descrição do job.</param>
    public void Initialize(JobId id, JobDescriptor descriptor)
    {
        Id = id;
        Descriptor = descriptor;
        State = JobState.Created;
        Attempt = 0;
        User = null;
        _items?.Clear();
    }

    /// <summary>Incrementa o número da tentativa.</summary>
    public void IncrementAttempt() => Attempt++;

    /// <summary>Limpa o estado para devolução ao pool.</summary>
    public void Reset()
    {
        Id = default;
        Descriptor = default!;
        State = JobState.Created;
        Attempt = 0;
        User = null;
        _items?.Clear();
    }
}
