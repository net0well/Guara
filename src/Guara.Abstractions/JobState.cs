namespace Guara.Abstractions;

/// <summary>Estados possíveis de um job no Guará.</summary>
public enum JobState
{
    /// <summary>Job aceito, ainda não enfileirado.</summary>
    Created,

    /// <summary>Na fila, aguardando dispatch.</summary>
    Enqueued,

    /// <summary>Agendado, com <c>NextRun</c> calculado (delay/cron/recurring).</summary>
    Scheduled,

    /// <summary>Em execução no pipeline.</summary>
    Processing,

    /// <summary>Concluído com sucesso.</summary>
    Succeeded,

    /// <summary>Falhou e esgotou as retentativas.</summary>
    Failed,

    /// <summary>Falhou e será reexecutado.</summary>
    Retrying,
}
