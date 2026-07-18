using Guara.Abstractions;
using Guara.Storage;

namespace Guara.Scheduler;

/// <summary>
/// Implementação do builder de calendários: acumula regras de exclusão (avaliadas em OU)
/// e valida as expressões cron na construção.
/// </summary>
internal sealed class CalendarBuilder : ICalendarBuilder
{
    private readonly List<DateOnly> _dates = [];
    private readonly List<CalendarDateRange> _ranges = [];
    private readonly HashSet<DayOfWeek> _daysOfWeek = [];
    private readonly List<string> _cronWindows = [];

    public ICalendarBuilder ExcluirData(DateOnly data)
    {
        _dates.Add(data);
        return this;
    }

    public ICalendarBuilder ExcluirIntervalo(DateOnly inicio, DateOnly fim)
    {
        if (fim < inicio)
        {
            throw new ArgumentException("O fim do intervalo excluído não pode ser anterior ao início.", nameof(fim));
        }

        _ranges.Add(new CalendarDateRange(inicio, fim));
        return this;
    }

    public ICalendarBuilder ExcluirDiasDaSemana(params DayOfWeek[] dias)
    {
        ArgumentNullException.ThrowIfNull(dias);
        foreach (var dia in dias)
        {
            _daysOfWeek.Add(dia);
        }

        return this;
    }

    public ICalendarBuilder ExcluirCron(string expressao)
    {
        CronExpression.Parse(expressao); // valida na chamada, com mensagem detalhada
        _cronWindows.Add(expressao);
        return this;
    }

    /// <summary>Materializa o calendário com as regras acumuladas.</summary>
    /// <param name="name">Nome único do calendário.</param>
    /// <returns>O calendário pronto para o upsert.</returns>
    public CalendarRecord Build(string name) => new()
    {
        Name = name,
        ExcludedDates = [.. _dates],
        ExcludedRanges = [.. _ranges],
        ExcludedDaysOfWeek = [.. _daysOfWeek],
        ExcludedCronWindows = [.. _cronWindows],
    };
}
