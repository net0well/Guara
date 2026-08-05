using System.Diagnostics;
using System.Globalization;
using Guara.Abstractions;
using Guara.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Guara.Throughput.Harness;

/// <summary>
/// Decompõe o custo de uma aquisição no PostgreSQL, para separar o que é ida-e-volta ao
/// banco do que é plano de consulta.
/// <para>
/// A vazão ponta a ponta mostrou o teto, não a causa. Sem esta separação, otimizar seria
/// escolher entre batelar as chamadas e corrigir a consulta sem saber qual das duas está
/// custando — e batelar uma consulta ruim multiplica o erro por N.
/// </para>
/// <para>
/// Medir em mais de uma profundidade é o que distingue as duas: custo de ida-e-volta é
/// constante, custo de plano cresce com a tabela.
/// </para>
/// </summary>
internal sealed class StorageProbe(string connectionString)
{
    private const string Queue = "default";
    private static readonly DateTimeOffset Agora = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    public async Task RunAsync(int[] profundidades, int amostras, CancellationToken ct)
    {
        Console.WriteLine("| Profundidade | Piso (SELECT 1) | CreateAsync | AcquireNextDueAsync | Aquisição ÷ piso |");
        Console.WriteLine("|---:|---:|---:|---:|---:|");

        var planos = new List<(int Profundidade, string Plano)>();

        foreach (var profundidade in profundidades)
        {
            var schema = $"g{Guid.NewGuid():n}"[..16];
            var services = new ServiceCollection();
            services.AddGuara().UsePostgreSqlStorage(connectionString, o => o.Schema = schema);

            await using var provider = services.BuildServiceProvider();
            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            var storage = provider.GetRequiredService<IStorage>();

            await SemearAsync(storage, profundidade, ct);

            var piso = await MedirPisoAsync(dataSource, amostras, ct);
            var criacao = await MedirCriacaoAsync(storage, amostras, profundidade, ct);
            var aquisicao = await MedirAquisicaoAsync(storage, amostras, ct);

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"| {profundidade:N0} | {piso:N0} µs | {criacao:N0} µs | {aquisicao:N0} µs | {aquisicao / piso:N1}× |"));

            planos.Add((profundidade, await ExplicarAsync(dataSource, schema, ct)));
        }

        foreach (var (profundidade, plano) in planos)
        {
            Console.WriteLine();
            Console.WriteLine($"--- plano da consulta de elegibilidade, profundidade {profundidade:N0} ---");
            Console.WriteLine(plano);
        }
    }

    private static async Task SemearAsync(IStorage storage, int profundidade, CancellationToken ct)
    {
        for (var i = 0; i < profundidade; i++)
        {
            await storage.Jobs.CreateAsync(new JobRecord
            {
                Id = new JobId($"seed-{i:D8}"),
                Descriptor = new JobDescriptor("Bench", "Nada", default, Queue),
                State = JobState.Enqueued,
                Queue = Queue,
                CreatedAt = Agora.AddSeconds(-profundidade + i),
            }, ct);
        }
    }

    /// <summary>
    /// O piso: uma ida-e-volta que não consulta nada. Tudo acima disto é trabalho do
    /// banco, não do caminho até ele.
    /// </summary>
    private static async Task<double> MedirPisoAsync(NpgsqlDataSource dataSource, int amostras, CancellationToken ct)
    {
        await using (var aquecimento = dataSource.CreateCommand("SELECT 1"))
        {
            await aquecimento.ExecuteScalarAsync(ct);
        }

        var relogio = Stopwatch.StartNew();
        for (var i = 0; i < amostras; i++)
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(ct);
        }

        return relogio.Elapsed.TotalMicroseconds / amostras;
    }

    private static async Task<double> MedirCriacaoAsync(
        IStorage storage, int amostras, int profundidade, CancellationToken ct)
    {
        var relogio = Stopwatch.StartNew();
        for (var i = 0; i < amostras; i++)
        {
            await storage.Jobs.CreateAsync(new JobRecord
            {
                Id = new JobId($"probe-{profundidade}-{i:D8}"),
                Descriptor = new JobDescriptor("Bench", "Nada", default, Queue),
                State = JobState.Scheduled,
                Queue = Queue,
                CreatedAt = Agora,
                // Data futura: entra na tabela sem virar candidato à aquisição, então
                // semear não muda a profundidade que a próxima medição enxerga.
                ScheduledFor = Agora.AddDays(30),
            }, ct);
        }

        return relogio.Elapsed.TotalMicroseconds / amostras;
    }

    private static async Task<double> MedirAquisicaoAsync(IStorage storage, int amostras, CancellationToken ct)
    {
        var relogio = Stopwatch.StartNew();
        for (var i = 0; i < amostras; i++)
        {
            await storage.Jobs.AcquireNextDueAsync(Queue, TimeSpan.FromMinutes(30), Agora, ct);
        }

        return relogio.Elapsed.TotalMicroseconds / amostras;
    }

    /// <summary>
    /// O plano da seleção de candidato. Espelha a consulta de
    /// <c>PostgreSqlJobStorage.AcquireNextDueAsync</c> sem o <c>FOR UPDATE SKIP LOCKED</c>,
    /// que travaria linhas — a forma do plano é a mesma, e o objetivo aqui é ver se há
    /// varredura sequencial ou ordenação.
    /// </summary>
    private static async Task<string> ExplicarAsync(NpgsqlDataSource dataSource, string schema, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand($"""
            EXPLAIN (ANALYZE, BUFFERS)
            SELECT id FROM {schema}.jobs
            WHERE queue = @queue
              AND (state = {(int)JobState.Enqueued}
                   OR (state IN ({(int)JobState.Scheduled}, {(int)JobState.Retrying}) AND scheduled_for <= @now)
                   OR (state = {(int)JobState.Processing} AND lease_until < @now))
            ORDER BY created_at
            LIMIT 1
            """);
        command.Parameters.AddWithValue("queue", Queue);
        command.Parameters.AddWithValue("now", Agora);

        var linhas = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            linhas.Add(reader.GetString(0));
        }

        return string.Join(Environment.NewLine, linhas);
    }
}
