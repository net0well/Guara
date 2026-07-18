namespace Guara.Abstractions;

/// <summary>
/// Builder fluente de calendários de exclusão. As regras são avaliadas em <b>OU</b>:
/// um instante é excluído se qualquer regra o cobrir. Datas e dias da semana valem
/// para o dia inteiro; janelas cron valem para o minuto exato.
/// </summary>
public interface ICalendarBuilder
{
    /// <summary>Exclui uma data (dia inteiro, no fuso do recorrente que usa o calendário).</summary>
    /// <param name="data">Data excluída.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    ICalendarBuilder ExcluirData(DateOnly data);

    /// <summary>Exclui um intervalo de datas (pontas inclusivas).</summary>
    /// <param name="inicio">Primeira data excluída.</param>
    /// <param name="fim">Última data excluída.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    ICalendarBuilder ExcluirIntervalo(DateOnly inicio, DateOnly fim);

    /// <summary>Exclui dias da semana (ex.: fins de semana).</summary>
    /// <param name="dias">Dias excluídos.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    ICalendarBuilder ExcluirDiasDaSemana(params DayOfWeek[] dias);

    /// <summary>Exclui os minutos que casam com uma expressão cron de 5 campos.</summary>
    /// <param name="expressao">Expressão cron da janela excluída.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    ICalendarBuilder ExcluirCron(string expressao);
}
