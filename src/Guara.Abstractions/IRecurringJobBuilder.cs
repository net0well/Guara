namespace Guara.Abstractions;

/// <summary>
/// Builder fluente de jobs recorrentes. Obrigatórios: <see cref="ComId"/>,
/// <see cref="Executa"/> e exatamente uma agenda (<see cref="ComCron"/> ou
/// <see cref="ACada"/>). A versão tipada de <c>Executa</c> (lambda) chega junto
/// dos source generators.
/// </summary>
public interface IRecurringJobBuilder
{
    /// <summary>Identificador estável da definição (chave do upsert).</summary>
    /// <param name="id">Id único do recorrente.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    IRecurringJobBuilder ComId(string id);

    /// <summary>O que executar em cada ocorrência.</summary>
    /// <param name="job">Descrição do job.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    IRecurringJobBuilder Executa(JobDescriptor job);

    /// <summary>Agenda por expressão cron de 5 campos (ex.: <c>"0 3 * * *"</c>).</summary>
    /// <param name="expressao">Expressão cron.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    IRecurringJobBuilder ComCron(string expressao);

    /// <summary>Agenda por intervalo fixo, ancorado em <see cref="IniciaEm"/> (ou na criação).</summary>
    /// <param name="intervalo">Intervalo entre ocorrências (positivo).</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    IRecurringJobBuilder ACada(TimeSpan intervalo);

    /// <summary>
    /// Janela diária do intervalo: ocorrências só disparam entre <paramref name="inicio"/> e
    /// <paramref name="fim"/> (início &gt; fim cruza a meia-noite). A janela governa o
    /// disparo — nunca interrompe uma execução em andamento.
    /// </summary>
    /// <param name="inicio">Início da janela (hora local do fuso configurado).</param>
    /// <param name="fim">Fim da janela.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    IRecurringJobBuilder EntreHorarios(TimeOnly inicio, TimeOnly fim);

    /// <summary>Fuso da agenda por id IANA (<c>America/Sao_Paulo</c>) ou Windows.</summary>
    /// <param name="fusoId">Id do fuso horário.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    IRecurringJobBuilder NoFusoHorario(string fusoId);

    /// <summary>Fuso da agenda.</summary>
    /// <param name="fuso">Fuso horário.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    IRecurringJobBuilder NoFusoHorario(TimeZoneInfo fuso);

    /// <summary>Nenhuma ocorrência antes deste instante (no passado, vale a próxima válida).</summary>
    /// <param name="inicio">Início da vigência.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    IRecurringJobBuilder IniciaEm(DateTimeOffset inicio);

    /// <summary>Nenhuma ocorrência depois deste instante (a definição expira, não é excluída).</summary>
    /// <param name="fim">Fim da vigência.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    IRecurringJobBuilder TerminaEm(DateTimeOffset fim);

    /// <summary>Descrição exibida no dashboard.</summary>
    /// <param name="descricao">Texto descritivo.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    IRecurringJobBuilder ComDescricao(string descricao);

    /// <summary>Fila das ocorrências (default: a fila do descriptor).</summary>
    /// <param name="fila">Nome da fila.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    IRecurringJobBuilder NaFila(string fila);

    /// <summary>Aplica um calendário de exclusões (precisa existir).</summary>
    /// <param name="nome">Nome do calendário.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    IRecurringJobBuilder ComCalendario(string nome);

    /// <summary>Pula a ocorrência se a anterior ainda não terminou (o pulo fica registrado).</summary>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    IRecurringJobBuilder PularSeAnteriorEmExecucao();
}
