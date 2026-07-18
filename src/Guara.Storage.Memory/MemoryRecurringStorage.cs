using Guara.Storage;

namespace Guara.Storage.Memory;

/// <summary>Definições recorrentes e calendários em memória, sob exclusão mútua.</summary>
internal sealed class MemoryRecurringStorage : IRecurringStorage
{
    private readonly object _sync = new();
    private readonly Dictionary<string, RecurringJobRecord> _recurring = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CalendarRecord> _calendars = new(StringComparer.Ordinal);

    public ValueTask UpsertAsync(RecurringJobRecord record, CancellationToken ct)
    {
        lock (_sync)
        {
            _recurring[record.Id] = record;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<RecurringJobRecord?> GetAsync(string id, CancellationToken ct)
    {
        lock (_sync)
        {
            return ValueTask.FromResult(_recurring.TryGetValue(id, out var record) ? record : null);
        }
    }

    public ValueTask<bool> DeleteAsync(string id, CancellationToken ct)
    {
        lock (_sync)
        {
            return ValueTask.FromResult(_recurring.Remove(id));
        }
    }

    public ValueTask<IReadOnlyList<RecurringJobRecord>> ListAsync(CancellationToken ct)
    {
        lock (_sync)
        {
            IReadOnlyList<RecurringJobRecord> snapshot =
                [.. _recurring.Values.OrderBy(r => r.Id, StringComparer.Ordinal)];
            return ValueTask.FromResult(snapshot);
        }
    }

    public ValueTask<IReadOnlyList<RecurringJobRecord>> ListDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        lock (_sync)
        {
            IReadOnlyList<RecurringJobRecord> due =
            [
                .. _recurring.Values
                    .Where(r => !r.Paused && r.NextRunAt is { } next && next <= now)
                    .OrderBy(r => r.NextRunAt),
            ];
            return ValueTask.FromResult(due);
        }
    }

    public ValueTask UpsertCalendarAsync(CalendarRecord calendar, CancellationToken ct)
    {
        lock (_sync)
        {
            _calendars[calendar.Name] = calendar;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<CalendarRecord?> GetCalendarAsync(string name, CancellationToken ct)
    {
        lock (_sync)
        {
            return ValueTask.FromResult(_calendars.TryGetValue(name, out var calendar) ? calendar : null);
        }
    }

    public ValueTask<bool> DeleteCalendarAsync(string name, CancellationToken ct)
    {
        lock (_sync)
        {
            return ValueTask.FromResult(_calendars.Remove(name));
        }
    }

    public ValueTask<IReadOnlyList<CalendarRecord>> ListCalendarsAsync(CancellationToken ct)
    {
        lock (_sync)
        {
            IReadOnlyList<CalendarRecord> snapshot =
                [.. _calendars.Values.OrderBy(c => c.Name, StringComparer.Ordinal)];
            return ValueTask.FromResult(snapshot);
        }
    }
}
