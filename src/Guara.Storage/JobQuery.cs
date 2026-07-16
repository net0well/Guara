using Guara.Abstractions;

namespace Guara.Storage;

/// <summary>
/// Consulta paginada de jobs (listagens do dashboard). Toda listagem é paginada e
/// limitada por <see cref="MaxPageSize"/> — nunca retorno ilimitado (spec 004/022).
/// </summary>
/// <param name="State">Filtra por estado, quando informado.</param>
/// <param name="Queue">Filtra por fila, quando informado.</param>
/// <param name="Page">Página, começando em 1.</param>
/// <param name="PageSize">Tamanho da página; providers aplicam o teto <see cref="MaxPageSize"/>.</param>
public sealed record JobQuery(
    JobState? State = null,
    string? Queue = null,
    int Page = 1,
    int PageSize = 50)
{
    /// <summary>Teto de tamanho de página aplicado por todos os providers.</summary>
    public const int MaxPageSize = 100;
}
