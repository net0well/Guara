using System.Text.Json;
using Guara.Abstractions;
using Guara.Storage;
using Microsoft.Data.SqlClient;

namespace Guara.Storage.SqlServer;

/// <summary>
/// Definições recorrentes e calendários. Agenda por intervalo e janela diária são
/// persistidas em ticks (round-trip exato de <see cref="TimeSpan"/>/<see cref="TimeOnly"/>);
/// o descriptor e o calendário inteiro vão como texto JSON.
/// </summary>
internal sealed class SqlServerRecurringStorage(
    SqlServerConnections connections, SqlServerSchemaInitializer schema, string s) : IRecurringStorage
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

        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {s}.recurring WITH (UPDLOCK, SERIALIZABLE)
            SET descriptor = @descriptor,
                cron = @cron,
                interval_ticks = @intervalTicks,
                window_start_ticks = @windowStartTicks,
                window_end_ticks = @windowEndTicks,
                time_zone = @timeZone,
                not_before = @notBefore,
                not_after = @notAfter,
                description = @description,
                queue = @queue,
                calendar_name = @calendarName,
                skip_if_previous_running = @skipIfPreviousRunning,
                paused = @paused,
                created_at = @createdAt,
                last_run_at = @lastRunAt,
                last_run_job_id = @lastRunJobId,
                next_run_at = @nextRunAt,
                last_skipped_at = @lastSkippedAt
            WHERE id = @id;

            IF @@ROWCOUNT = 0
            INSERT INTO {s}.recurring ({Columns})
            SELECT {Values}
            WHERE NOT EXISTS (SELECT 1 FROM {s}.recurring WITH (UPDLOCK, SERIALIZABLE) WHERE id = @id);
            """;
        command.Parameters.AddWithValue("@id", record.Id);
        command.Parameters.AddWithValue(
            "@descriptor", JsonSerializer.Serialize(record.Descriptor, SqlServerJsonContext.Default.JobDescriptor));
        command.Parameters.AddWithValue("@cron", (object?)record.CronExpression ?? DBNull.Value);
        command.Parameters.AddWithValue("@intervalTicks", (object?)record.Interval?.Ticks ?? DBNull.Value);
        command.Parameters.AddWithValue("@windowStartTicks", (object?)record.WindowStart?.Ticks ?? DBNull.Value);
        command.Parameters.AddWithValue("@windowEndTicks", (object?)record.WindowEnd?.Ticks ?? DBNull.Value);
        command.Parameters.AddWithValue("@timeZone", (object?)record.TimeZoneId ?? DBNull.Value);
        command.Parameters.AddWithValue("@notBefore", (object?)record.NotBefore ?? DBNull.Value);
        command.Parameters.AddWithValue("@notAfter", (object?)record.NotAfter ?? DBNull.Value);
        command.Parameters.AddWithValue("@description", (object?)record.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@queue", record.Queue);
        command.Parameters.AddWithValue("@calendarName", (object?)record.CalendarName ?? DBNull.Value);
        command.Parameters.AddWithValue("@skipIfPreviousRunning", record.SkipIfPreviousRunning);
        command.Parameters.AddWithValue("@paused", record.Paused);
        command.Parameters.AddWithValue("@createdAt", record.CreatedAt);
        command.Parameters.AddWithValue("@lastRunAt", (object?)record.LastRunAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@lastRunJobId", (object?)record.LastRunJobId?.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("@nextRunAt", (object?)record.NextRunAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@lastSkippedAt", (object?)record.LastSkippedAt ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<RecurringJobRecord?> GetAsync(string id, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM {s}.recurring WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRecurring(reader) : null;
    }

    public async ValueTask<bool> DeleteAsync(string id, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {s}.recurring WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async ValueTask<IReadOnlyList<RecurringJobRecord>> ListAsync(CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM {s}.recurring ORDER BY id";
        return await ReadAllAsync(command, ct);
    }

    public async ValueTask<IReadOnlyList<RecurringJobRecord>> ListDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Columns} FROM {s}.recurring
            WHERE paused = 0 AND next_run_at IS NOT NULL AND next_run_at <= @now
            ORDER BY next_run_at
            """;
        command.Parameters.AddWithValue("@now", now);
        return await ReadAllAsync(command, ct);
    }

    public async ValueTask UpsertCalendarAsync(CalendarRecord calendar, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        await schema.EnsureAsync(ct);

        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {s}.calendars WITH (UPDLOCK, SERIALIZABLE) SET payload = @payload WHERE name = @name;

            IF @@ROWCOUNT = 0
            INSERT INTO {s}.calendars (name, payload)
            SELECT @name, @payload
            WHERE NOT EXISTS (SELECT 1 FROM {s}.calendars WITH (UPDLOCK, SERIALIZABLE) WHERE name = @name);
            """;
        command.Parameters.AddWithValue("@name", calendar.Name);
        command.Parameters.AddWithValue(
            "@payload", JsonSerializer.Serialize(calendar, SqlServerJsonContext.Default.CalendarRecord));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async ValueTask<CalendarRecord?> GetCalendarAsync(string name, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload FROM {s}.calendars WHERE name = @name";
        command.Parameters.AddWithValue("@name", name);

        var payload = (string?)await command.ExecuteScalarAsync(ct);
        return payload is null
            ? null
            : JsonSerializer.Deserialize(payload, SqlServerJsonContext.Default.CalendarRecord);
    }

    public async ValueTask<bool> DeleteCalendarAsync(string name, CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {s}.calendars WHERE name = @name";
        command.Parameters.AddWithValue("@name", name);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    public async ValueTask<IReadOnlyList<CalendarRecord>> ListCalendarsAsync(CancellationToken ct)
    {
        await schema.EnsureAsync(ct);
        await using var connection = await connections.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload FROM {s}.calendars ORDER BY name";

        var results = new List<CalendarRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(JsonSerializer.Deserialize(
                reader.GetString(0), SqlServerJsonContext.Default.CalendarRecord)!);
        }

        return results;
    }

    private static async ValueTask<IReadOnlyList<RecurringJobRecord>> ReadAllAsync(
        SqlCommand command, CancellationToken ct)
    {
        var results = new List<RecurringJobRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadRecurring(reader));
        }

        return results;
    }

    private static RecurringJobRecord ReadRecurring(SqlDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Descriptor = JsonSerializer.Deserialize(reader.GetString(1), SqlServerJsonContext.Default.JobDescriptor)!,
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
