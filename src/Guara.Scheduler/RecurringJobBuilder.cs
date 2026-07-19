using Guara.Abstractions;
using Guara.Storage;

namespace Guara.Scheduler;

/// <summary>
/// Implementação do builder de recorrentes: acumula a configuração e valida tudo
/// no <see cref="Build"/> — id e job obrigatórios, exatamente uma agenda, janela só
/// com intervalo, cron e vigência consistentes. Falha na chamada, nunca em runtime.
/// </summary>
internal sealed class RecurringJobBuilder : IRecurringJobBuilder
{
    private string? _id;
    private JobDescriptor? _descriptor;
    private string? _cron;
    private TimeSpan? _interval;
    private TimeOnly? _windowStart;
    private TimeOnly? _windowEnd;
    private string? _timeZoneId;
    private DateTimeOffset? _notBefore;
    private DateTimeOffset? _notAfter;
    private string? _description;
    private string? _queue;
    private string? _calendarName;
    private bool _skipIfPreviousRunning;

    public IRecurringJobBuilder ComId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _id = id;
        return this;
    }

    public IRecurringJobBuilder Executa(JobDescriptor job)
    {
        ArgumentNullException.ThrowIfNull(job);
        _descriptor = job;
        return this;
    }

    public IRecurringJobBuilder ComCron(string expressao)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expressao);
        _cron = expressao;
        return this;
    }

    public IRecurringJobBuilder ACada(TimeSpan intervalo)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(intervalo, TimeSpan.Zero);
        _interval = intervalo;
        return this;
    }

    public IRecurringJobBuilder EntreHorarios(TimeOnly inicio, TimeOnly fim)
    {
        if (inicio == fim)
        {
            throw new ArgumentException("A janela diária precisa de início e fim diferentes.", nameof(fim));
        }

        _windowStart = inicio;
        _windowEnd = fim;
        return this;
    }

    public IRecurringJobBuilder NoFusoHorario(string fusoId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fusoId);
        _timeZoneId = fusoId;
        return this;
    }

    public IRecurringJobBuilder NoFusoHorario(TimeZoneInfo fuso)
    {
        ArgumentNullException.ThrowIfNull(fuso);
        _timeZoneId = fuso.Id;
        return this;
    }

    public IRecurringJobBuilder IniciaEm(DateTimeOffset inicio)
    {
        _notBefore = inicio;
        return this;
    }

    public IRecurringJobBuilder TerminaEm(DateTimeOffset fim)
    {
        _notAfter = fim;
        return this;
    }

    public IRecurringJobBuilder ComDescricao(string descricao)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(descricao);
        _description = descricao;
        return this;
    }

    public IRecurringJobBuilder NaFila(string fila)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fila);
        _queue = fila;
        return this;
    }

    public IRecurringJobBuilder ComCalendario(string nome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nome);
        _calendarName = nome;
        return this;
    }

    public IRecurringJobBuilder PularSeAnteriorEmExecucao()
    {
        _skipIfPreviousRunning = true;
        return this;
    }

    /// <summary>Valida a configuração e materializa a definição (campos de execução ainda vazios).</summary>
    /// <param name="createdAt">Instante de criação atribuído à definição.</param>
    /// <returns>A definição pronta para o upsert.</returns>
    public RecurringJobRecord Build(DateTimeOffset createdAt)
    {
        if (_id is null)
        {
            throw new InvalidOperationException("Recorrente sem identidade: chame ComId(\"...\").");
        }

        if (_descriptor is null)
        {
            throw new InvalidOperationException($"Recorrente '{_id}' sem job: chame Executa(...).");
        }

        if (_cron is null == _interval is null)
        {
            throw new InvalidOperationException(
                $"Recorrente '{_id}' precisa de exatamente uma agenda: ComCron(\"...\") ou ACada(intervalo).");
        }

        if (_windowStart is not null && _interval is null)
        {
            throw new InvalidOperationException(
                $"Recorrente '{_id}': EntreHorarios só se aplica à agenda por intervalo (ACada).");
        }

        if (_cron is not null && !CronExpression.TryParse(_cron, out _))
        {
            CronExpression.Parse(_cron); // relança com a mensagem detalhada do campo inválido
        }

        if (_notBefore is { } start && _notAfter is { } end && end <= start)
        {
            throw new InvalidOperationException(
                $"Recorrente '{_id}': TerminaEm precisa ser posterior a IniciaEm.");
        }

        TimeZones.Resolve(_timeZoneId);

        // O job pode declarar [GuaraPularSeAnteriorEmExecucao]: a factory gerada marca
        // o descriptor e o builder honra sem exigir a chamada fluente.
        var skipIfPreviousRunning = _skipIfPreviousRunning
            || _descriptor.Metadata?.ContainsKey(JobMetadataKeys.SkipIfPreviousRunning) is true;

        return new RecurringJobRecord
        {
            Id = _id,
            Descriptor = _descriptor,
            CronExpression = _cron,
            Interval = _interval,
            WindowStart = _windowStart,
            WindowEnd = _windowEnd,
            TimeZoneId = _timeZoneId,
            NotBefore = _notBefore,
            NotAfter = _notAfter,
            Description = _description,
            Queue = _queue ?? _descriptor.Queue,
            CalendarName = _calendarName,
            SkipIfPreviousRunning = skipIfPreviousRunning,
            CreatedAt = createdAt,
        };
    }
}
