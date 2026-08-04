using Guara.Abstractions;

namespace Guara.Storage;

/// <summary>
/// Consulta paginada de jobs (listagens e busca do dashboard). Toda listagem é paginada e
/// limitada por <see cref="MaxPageSize"/> — nunca retorno ilimitado. Filtros informados
/// combinam em <b>E</b>; os nulos não restringem nada.
/// </summary>
/// <param name="State">Filtra por estado, quando informado.</param>
/// <param name="Queue">Filtra por fila, quando informado.</param>
/// <param name="Page">Página, começando em 1.</param>
/// <param name="PageSize">Tamanho da página; providers aplicam o teto <see cref="MaxPageSize"/>.</param>
/// <param name="Text">
/// Busca livre, sem diferenciar maiúsculas: casa por trecho no id, no tipo e no método do
/// job. É o campo que o operador digita quando só lembra parte do nome.
/// </param>
/// <param name="TypeName">Filtra pelo tipo declarante do job (comparação exata).</param>
/// <param name="From">Limite inferior de <see cref="JobRecord.CreatedAt"/>, inclusivo.</param>
/// <param name="To">Limite superior de <see cref="JobRecord.CreatedAt"/>, exclusivo.</param>
public sealed record JobQuery(
    JobState? State = null,
    string? Queue = null,
    int Page = 1,
    int PageSize = 50,
    string? Text = null,
    string? TypeName = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null)
{
    /// <summary>Teto de tamanho de página aplicado por todos os providers.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Página efetiva, já normalizada (nunca menor que 1).</summary>
    public int EffectivePage => Math.Max(1, Page);

    /// <summary>Tamanho de página efetivo, já limitado por <see cref="MaxPageSize"/>.</summary>
    public int EffectivePageSize => Math.Clamp(PageSize, 1, MaxPageSize);

    /// <summary>
    /// Avalia os filtros contra um registro. Vive aqui para que os providers em memória
    /// e a conformance compartilhem exatamente a mesma semântica de casamento — providers
    /// SQL traduzem estes mesmos critérios para a consulta.
    /// </summary>
    /// <param name="record">Registro candidato.</param>
    /// <returns><c>true</c> quando o registro satisfaz todos os filtros informados.</returns>
    public bool Matches(JobRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (State is { } state && record.State != state)
        {
            return false;
        }

        if (Queue is not null && !string.Equals(record.Queue, Queue, StringComparison.Ordinal))
        {
            return false;
        }

        if (TypeName is not null
            && !string.Equals(record.Descriptor.TypeName, TypeName, StringComparison.Ordinal))
        {
            return false;
        }

        if (From is { } from && record.CreatedAt < from)
        {
            return false;
        }

        if (To is { } to && record.CreatedAt >= to)
        {
            return false;
        }

        if (Text is { Length: > 0 } text)
        {
            var hit = record.Id.Value.Contains(text, StringComparison.OrdinalIgnoreCase)
                || record.Descriptor.TypeName.Contains(text, StringComparison.OrdinalIgnoreCase)
                || record.Descriptor.MethodName.Contains(text, StringComparison.OrdinalIgnoreCase);
            if (!hit)
            {
                return false;
            }
        }

        return true;
    }
}
