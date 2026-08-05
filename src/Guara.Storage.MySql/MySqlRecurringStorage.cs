using System.Text.Json;
using Guara.Abstractions;
using Guara.Storage;
using MySqlConnector;

namespace Guara.Storage.MySql;

/// <summary>
/// Definições recorrentes e calendários. Agenda por intervalo e janela diária são
/// persistidas em ticks (round-trip exato de <see cref="TimeSpan"/>/<see cref="TimeOnly"/>);
/// o descriptor e o calendário inteiro vão como texto JSON.
/// </summary>
internal sealed class MySqlRecurringStorage(
    MySqlDataSource dataSource, MySqlSchemaInitializer schema, string p) : IRecurringStorage
{
    private const string Columns =
        "id, descriptor, cron, interval_ticks, window_start_ticks, window_end_ticks, time_zone, " +
        "not_before, not_after, description, queue, calendar_name, skip_if_previous_running, paused, " +
        "created_at, last_run_at, last_run_job_id, next_run_at, last_skipped_at";

    private const string Values =
        "@id, @descriptor, @cron, @intervalTicks, @windowStartTicks, @windowEndTicks, @timeZone, " +
        "@notBefore, @notAfter, @description, @queue, @calendarName, @skipIfPreviousRunning, @paused, " +
        "@createdAt, @lastRunAt, @lastRunJobId, @nextRunAt, @lastSkippedAt";

    public async ValueTask UpsertAsync(RecurringJobRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        await schema.EnsureAsync(ct);

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {p}recurring ({Columns})
            VALUES ({Values})
            ON DUPLICATE KEY UPDATE
                descriptor = VALUES(descriptor),
                cron = VALUES(cron),
                interval_ticks = VALUES(interval_ticks),
                window_start_ticks = VALUES(window_start_ticks),
                window_end_ticks = VALUES(window_end_ticks),
                time_zone = VALUES(time_zone),
                not_before = VALUES(not_before),
                not_after = VALUES(not_after),
                description = VALUES(description),
                queue = VALUES(queue),
                calendar_name = VALUES(calendar_name),
                skip_if_previous_running = VALUES(skip_if_previous_running),
                paused = VALUES(paused),
                created_at = VALUES(created_at),
                last_run_at = VALUES(last_run_at),
                last_run_job_id = VALUES(last_run_job_id),
                next_run_at = VALUES(next_run_at),
                last_skipped_at = VALUES(last_skipped_at)
            """;
        command.Parameters.AddWithValue("@id", record.Id);
        command.Parameters.AddWithValue(
            "@descriptor", JsonSerializer.Serialize(record.Descriptor, MySqlJsonContext.Default.JobDescriptor));
        command.Parameters.AddWithValue("@cron", (object?)record.CronExpression ?? DBNull.Value);
        command.Parameters.AddWithValue("@intervalTicks", (object?)record.Interval?.Ticks ?? DBNull.Value);
        command.Parameters.AddWithValue("@windowStartTicks", (object?)record.WindowStart?.Ticks ?? DBNull.Value);
        command.Parameters.AddWithValue("@windowEndTicks", (object?)record.WindowEnd?.Ticks ?? DBNull.Value);
        command.Parameters.AddWithValue("@timeZone", (object?)record.TimeZoneId ?? DBNull.Value);
        command.Parameters.AddWithValue("@notBefore", MySqlTime.ToDatabaseOrNull(record.NotBefore));
        command.Parameters.AddWithValue("@notAfter", MySqlTime.ToDatabaseOrNull(record.NotAfter));
        command.Parameters.AddWithValue("@description", (object?)record.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@queue", record.Queue);
        command.Parameters.AddWithValue("@calendarName", (object?)record.CalendarName ?? DBNull.Value);
        command.Parameters.AddWithValue("@skipIfPreviousRunning", record.SkipIfPreviousRunning);
        command.Parameters.AddWithValue("@paused", record.Paused);
        command.Parameters.AddWithValue("@createdAt", MySqlTime.ToDatabase(record.CreatedAt));
        command.Parameters.AddWithValue("@lastRunAt", MySqlTime.ToDatabaseOrNull(record.LastRunAt));
        command.Parameters.AddWithValue("@lastRunJobId", (object?)record.LastRunJobId?.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("@nextRunAt", MySqlTime.ToDatabaseOrNull(record.NextRunAt));
        command.Parameters.AddWithValue("@lastSkippedAt", MySqlTime.ToDatabaseOrNull(record.LastSkippedAt));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<RecurringJobRecord?> GetAsync(string id, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM {p}recurring WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRecurring(reader) : null;
    }

    public async ValueTask<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {p}recurring WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async ValueTask<IReadOnlyList<RecurringJobRecord>> ListAsync(CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM {p}recurring ORDER BY id";
        return await ReadAllAsync(command, ct);
    }

    public async ValueTask<IReadOnlyList<RecurringJobRecord>> ListDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Columns} FROM {p}recurring
            WHERE paused = 0 AND next_run_at IS NOT NULL AND next_run_at <= @now
            ORDER BY next_run_at
            """;
        command.Parameters.AddWithValue("@now", MySqlTime.ToDatabase(now));
        return await ReadAllAsync(command, ct);
    }

    public async ValueTask UpsertCalendarAsync(CalendarRecord calendar, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        await schema.EnsureAsync(ct);

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {p}calendars (name, payload)
            VALUES (@name, @payload)
            ON DUPLICATE KEY UPDATE payload = VALUES(payload)
            """;
        command.Parameters.AddWithValue("@name", calendar.Name);
        command.Parameters.AddWithValue(
            "@payload", JsonSerializer.Serialize(calendar, MySqlJsonContext.Default.CalendarRecord));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<CalendarRecord?> GetCalendarAsync(string name, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload FROM {p}calendars WHERE name = @name";
        command.Parameters.AddWithValue("@name", name);

        var payload = (string?)await command.ExecuteScalarAsync(ct);
        return payload is null
            ? null
            : JsonSerializer.Deserialize(payload, MySqlJsonContext.Default.CalendarRecord);
    }

    public async ValueTask<bool> DeleteCalendarAsync(string name, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {p}calendars WHERE name = @name";
        command.Parameters.AddWithValue("@name", name);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async ValueTask<IReadOnlyList<CalendarRecord>> ListCalendarsAsync(CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload FROM {p}calendars ORDER BY name";

        var results = new List<CalendarRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(JsonSerializer.Deserialize(
                reader.GetString(0), MySqlJsonContext.Default.CalendarRecord)!);
        }

        return results;
    }

    private static async ValueTask<IReadOnlyList<RecurringJobRecord>> ReadAllAsync(
        MySqlCommand command, CancellationToken ct)
    {
        var results = new List<RecurringJobRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadRecurring(reader));
        }

        return results;
    }

    private static RecurringJobRecord ReadRecurring(MySqlDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Descriptor = JsonSerializer.Deserialize(reader.GetString(1), MySqlJsonContext.Default.JobDescriptor)!,
        CronExpression = reader.IsDBNull(2) ? null : reader.GetString(2),
        Interval = reader.IsDBNull(3) ? null : TimeSpan.FromTicks(reader.GetInt64(3)),
        WindowStart = reader.IsDBNull(4) ? null : new TimeOnly(reader.GetInt64(4)),
        WindowEnd = reader.IsDBNull(5) ? null : new TimeOnly(reader.GetInt64(5)),
        TimeZoneId = reader.IsDBNull(6) ? null : reader.GetString(6),
        NotBefore = reader.IsDBNull(7) ? null : MySqlTime.FromDatabase(reader.GetDateTime(7)),
        NotAfter = reader.IsDBNull(8) ? null : MySqlTime.FromDatabase(reader.GetDateTime(8)),
        Description = reader.IsDBNull(9) ? null : reader.GetString(9),
        Queue = reader.GetString(10),
        CalendarName = reader.IsDBNull(11) ? null : reader.GetString(11),
        SkipIfPreviousRunning = reader.GetBoolean(12),
        Paused = reader.GetBoolean(13),
        CreatedAt = MySqlTime.FromDatabase(reader.GetDateTime(14)),
        LastRunAt = reader.IsDBNull(15) ? null : MySqlTime.FromDatabase(reader.GetDateTime(15)),
        LastRunJobId = reader.IsDBNull(16) ? null : new JobId(reader.GetString(16)),
        NextRunAt = reader.IsDBNull(17) ? null : MySqlTime.FromDatabase(reader.GetDateTime(17)),
        LastSkippedAt = reader.IsDBNull(18) ? null : MySqlTime.FromDatabase(reader.GetDateTime(18)),
    };
}
