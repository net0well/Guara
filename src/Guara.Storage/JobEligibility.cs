using Guara.Abstractions;

namespace Guara.Storage;

/// <summary>
/// A regra única de <b>quando</b> um job passa a poder ser adquirido, materializada como
/// instante em vez de derivada do estado na hora da consulta.
/// <para>
/// Vive aqui, e não em cada provider, porque divergir nesta regra seria dois storages com
/// semânticas diferentes sob o mesmo contrato — e a diferença só apareceria em produção,
/// como job que roda fora de ordem ou que nunca roda.
/// </para>
/// </summary>
public static class JobEligibility
{
    /// <summary>Instante em que o job fica elegível, ou <c>null</c> quando nunca fica.</summary>
    /// <param name="state">Estado do job.</param>
    /// <param name="createdAt">Quando o job foi criado.</param>
    /// <param name="scheduledFor">Quando deve rodar, para agendado e retentativa.</param>
    /// <param name="leaseUntil">Até quando a posse vale, para job em execução.</param>
    /// <returns>O instante de elegibilidade.</returns>
    public static DateTimeOffset? For(
        JobState state, DateTimeOffset createdAt, DateTimeOffset? scheduledFor, DateTimeOffset? leaseUntil)
        => state switch
        {
            // Elegível já; usar a criação preserva a ordem entre os enfileirados.
            JobState.Enqueued => createdAt,

            // Sem data marcada é continuação esperando o pai: nunca elegível até o gatilho.
            JobState.Scheduled or JobState.Retrying => scheduledFor,

            // Elegível quando a posse expira, ou seja, quando o job foi abandonado.
            JobState.Processing => leaseUntil,

            // Terminal: não volta para a fila.
            _ => null,
        };

    /// <summary>Instante em que o job fica elegível, ou <c>null</c> quando nunca fica.</summary>
    /// <param name="record">Registro do job.</param>
    /// <returns>O instante de elegibilidade.</returns>
    public static DateTimeOffset? For(JobRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return For(record.State, record.CreatedAt, record.ScheduledFor, record.LeaseUntil);
    }
}
