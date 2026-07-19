using System.Text.Json;
using Guara.Abstractions;
using Guara.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Guara.Storage.PostgreSql;

/// <summary>
/// Definições recorrentes e calendários. Agenda por intervalo e janela diária são
/// persistidas em ticks (round-trip exato de <see cref="TimeSpan"/>/<see cref="TimeOnly"/>);
/// o descriptor e o calendário inteiro vão como <c>jsonb</c>.
/// </summary>
internal sealed class PostgreSqlRecurringStorage(
    NpgsqlDataSource dataSource, PostgreSqlSchemaInitializer schema, string s) : IRecurringStorage
{
    private const string Columns =
        "id, descriptor, cron, interval_ticks, window_start_ticks, window_end_ticks, time_zone, " +
        "not_before, not_after, description, queue, calendar_name, skip_if_previous_running, paused, " +
        "created_at, last_run_at, last_run_job_id, next_run_at, last_skipped_at";

    public async ValueTask UpsertAsync(RecurringJobRecord record, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"""
            INSERT INTO {s}.recurring ({Columns})
            VALUES (@id, @descriptor, @cron, @intervalTicks, @windowStartTicks, @windowEndTicks, @timeZone,
                    @notBefore, @notAfter, @description, @queue, @calendarName, @skipIfPreviousRunning, @paused,
                    @createdAt, @lastRunAt, @lastRunJobId, @nextRunAt, @lastSkippedAt)
            ON CONFLICT (id) DO UPDATE SET
                descriptor = EXCLUDED.descriptor,
                cron = EXCLUDED.cron,
                interval_ticks = EXCLUDED.interval_ticks,
                window_start_ticks = EXCLUDED.window_start_ticks,
                window_end_ticks = EXCLUDED.window_end_ticks,
                time_zone = EXCLUDED.time_zone,
                not_before = EXCLUDED.not_before,
                not_after = EXCLUDED.not_after,
                description = EXCLUDED.description,
                queue = EXCLUDED.queue,
                calendar_name = EXCLUDED.calendar_name,
                skip_if_previous_running = EXCLUDED.skip_if_previous_running,
                paused = EXCLUDED.paused,
                created_at = EXCLUDED.created_at,
                last_run_at = EXCLUDED.last_run_at,
                last_run_job_id = EXCLUDED.last_run_job_id,
                next_run_at = EXCLUDED.next_run_at,
                last_skipped_at = EXCLUDED.last_skipped_at
            """);
        command.Parameters.AddWithValue("id", record.Id);
        command.Parameters.Add(new NpgsqlParameter("descriptor", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(record.Descriptor, PostgreSqlJsonContext.Default.JobDescriptor),
        });
        command.Parameters.AddWithValue("cron", (object?)record.CronExpression ?? DBNull.Value);
        command.Parameters.AddWithValue("intervalTicks", (object?)record.Interval?.Ticks ?? DBNull.Value);
        command.Parameters.AddWithValue("windowStartTicks", (object?)record.WindowStart?.Ticks ?? DBNull.Value);
        command.Parameters.AddWithValue("windowEndTicks", (object?)record.WindowEnd?.Ticks ?? DBNull.Value);
        command.Parameters.AddWithValue("timeZone", (object?)record.TimeZoneId ?? DBNull.Value);
        command.Parameters.AddWithValue("notBefore", (object?)record.NotBefore ?? DBNull.Value);
        command.Parameters.AddWithValue("notAfter", (object?)record.NotAfter ?? DBNull.Value);
        command.Parameters.AddWithValue("description", (object?)record.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("queue", record.Queue);
        command.Parameters.AddWithValue("calendarName", (object?)record.CalendarName ?? DBNull.Value);
        command.Parameters.AddWithValue("skipIfPreviousRunning", record.SkipIfPreviousRunning);
        command.Parameters.AddWithValue("paused", record.Paused);
        command.Parameters.AddWithValue("createdAt", record.CreatedAt);
        command.Parameters.AddWithValue("lastRunAt", (object?)record.LastRunAt ?? DBNull.Value);
        command.Parameters.AddWithValue("lastRunJobId", (object?)record.LastRunJobId?.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("nextRunAt", (object?)record.NextRunAt ?? DBNull.Value);
        command.Parameters.AddWithValue("lastSkippedAt", (object?)record.LastSkippedAt ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<RecurringJobRecord?> GetAsync(string id, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand(
            $"SELECT {Columns} FROM {s}.recurring WHERE id = @id");
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRecurring(reader) : null;
    }

    public async ValueTask<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"DELETE FROM {s}.recurring WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async ValueTask<IReadOnlyList<RecurringJobRecord>> ListAsync(CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand(
            $"SELECT {Columns} FROM {s}.recurring ORDER BY id");
        return await ReadAllAsync(command, ct);
    }

    public async ValueTask<IReadOnlyList<RecurringJobRecord>> ListDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"""
            SELECT {Columns} FROM {s}.recurring
            WHERE paused = false AND next_run_at IS NOT NULL AND next_run_at <= @now
            ORDER BY next_run_at
            """);
        command.Parameters.AddWithValue("now", now);
        return await ReadAllAsync(command, ct);
    }

    public async ValueTask UpsertCalendarAsync(CalendarRecord calendar, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"""
            INSERT INTO {s}.calendars (name, payload) VALUES (@name, @payload)
            ON CONFLICT (name) DO UPDATE SET payload = EXCLUDED.payload
            """);
        command.Parameters.AddWithValue("name", calendar.Name);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(calendar, PostgreSqlJsonContext.Default.CalendarRecord),
        });
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<CalendarRecord?> GetCalendarAsync(string name, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand(
            $"SELECT payload FROM {s}.calendars WHERE name = @name");
        command.Parameters.AddWithValue("name", name);

        var payload = (string?)await command.ExecuteScalarAsync(ct);
        return payload is null
            ? null
            : JsonSerializer.Deserialize(payload, PostgreSqlJsonContext.Default.CalendarRecord);
    }

    public async ValueTask<bool> DeleteCalendarAsync(string name, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand($"DELETE FROM {s}.calendars WHERE name = @name");
        command.Parameters.AddWithValue("name", name);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async ValueTask<IReadOnlyList<CalendarRecord>> ListCalendarsAsync(CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var command = dataSource.CreateCommand(
            $"SELECT payload FROM {s}.calendars ORDER BY name");

        var results = new List<CalendarRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(JsonSerializer.Deserialize(
                reader.GetString(0), PostgreSqlJsonContext.Default.CalendarRecord)!);
        }

        return results;
    }

    private static async ValueTask<IReadOnlyList<RecurringJobRecord>> ReadAllAsync(
        NpgsqlCommand command, CancellationToken ct)
    {
        var results = new List<RecurringJobRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadRecurring(reader));
        }

        return results;
    }

    private static RecurringJobRecord ReadRecurring(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Descriptor = JsonSerializer.Deserialize(reader.GetString(1), PostgreSqlJsonContext.Default.JobDescriptor)!,
        CronExpression = reader.IsDBNull(2) ? null : reader.GetString(2),
        Interval = reader.IsDBNull(3) ? null : TimeSpan.FromTicks(reader.GetInt64(3)),
        WindowStart = reader.IsDBNull(4) ? null : new TimeOnly(reader.GetInt64(4)),
        WindowEnd = reader.IsDBNull(5) ? null : new TimeOnly(reader.GetInt64(5)),
        TimeZoneId = reader.IsDBNull(6) ? null : reader.GetString(6),
        NotBefore = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
        NotAfter = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
        Description = reader.IsDBNull(9) ? null : reader.GetString(9),
        Queue = reader.GetString(10),
        CalendarName = reader.IsDBNull(11) ? null : reader.GetString(11),
        SkipIfPreviousRunning = reader.GetBoolean(12),
        Paused = reader.GetBoolean(13),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(14),
        LastRunAt = reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15),
        LastRunJobId = reader.IsDBNull(16) ? null : new JobId(reader.GetString(16)),
        NextRunAt = reader.IsDBNull(17) ? null : reader.GetFieldValue<DateTimeOffset>(17),
        LastSkippedAt = reader.IsDBNull(18) ? null : reader.GetFieldValue<DateTimeOffset>(18),
    };
}
