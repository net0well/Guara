using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using Guara.Abstractions;
using Guara.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Npgsql;

namespace Guara.Throughput.Harness;

/// <summary>
/// Decompõe o custo de uma aquisição, para separar o que é ida-e-volta ao banco do que é
/// plano de consulta.
/// <para>
/// A vazão ponta a ponta mostra o teto, não a causa. Sem esta separação, otimizar seria
/// escolher entre batelar as chamadas e corrigir a consulta sem saber qual das duas está
/// custando — e batelar uma consulta ruim multiplica o erro por N.
/// </para>
/// <para>
/// Medir em mais de uma profundidade é o que distingue as duas: custo de ida-e-volta é
/// constante, custo de plano cresce com a tabela.
/// </para>
/// </summary>
internal sealed class StorageProbe(StorageKind storage, string connectionString)
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
            var isolamento = $"g{Guid.NewGuid():n}"[..12];
            var services = new ServiceCollection();
            Registrar(services.AddGuara(), isolamento);

            await using var provider = services.BuildServiceProvider();
            await using var connection = NovaConexao();
            await connection.OpenAsync(ct);

            var jobs = provider.GetRequiredService<IStorage>();
            await SemearAsync(jobs, profundidade, ct);

            var piso = await MedirAsync(amostras, async () =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                await command.ExecuteScalarAsync(ct);
            });

            var criacao = await MedirAsync(amostras, async () =>
                await jobs.Jobs.CreateAsync(NovoJob($"probe-{Guid.NewGuid():n}", futuro: true), ct));

            var aquisicao = await MedirAsync(amostras, async () =>
                await jobs.Jobs.AcquireNextDueAsync(Queue, 1, TimeSpan.FromMinutes(30), Agora, ct));

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"| {profundidade:N0} | {piso:N0} µs | {criacao:N0} µs | {aquisicao:N0} µs | {aquisicao / piso:N1}× |"));

            planos.Add((profundidade, await ExplicarAsync(connection, isolamento, ct)));
        }

        foreach (var (profundidade, plano) in planos)
        {
            Console.WriteLine();
            Console.WriteLine($"--- plano da consulta de elegibilidade, profundidade {profundidade:N0} ---");
            Console.WriteLine(plano);
        }
    }

    private void Registrar(IGuaraBuilder builder, string isolamento)
    {
        _ = storage switch
        {
            StorageKind.PostgreSql => builder.UsePostgreSqlStorage(connectionString, o => o.Schema = isolamento),
            StorageKind.SqlServer => builder.UseSqlServerStorage(connectionString, o => o.Schema = isolamento),
            StorageKind.MySql => builder.UseMySqlStorage(connectionString, o => o.TablePrefix = isolamento + "_"),
            _ => throw new ArgumentOutOfRangeException(nameof(isolamento), storage, "Sonda só cobre bancos relacionais."),
        };
    }

    private DbConnection NovaConexao() => storage switch
    {
        StorageKind.PostgreSql => new NpgsqlConnection(connectionString),
        StorageKind.SqlServer => new SqlConnection(connectionString),
        StorageKind.MySql => new MySqlConnection(connectionString),
        _ => throw new ArgumentOutOfRangeException(nameof(storage), storage, "Sonda só cobre bancos relacionais."),
    };

    private static JobRecord NovoJob(string id, bool futuro = false) => new()
    {
        Id = new JobId(id),
        Descriptor = new JobDescriptor("Bench", "Nada", default, Queue),
        // Data futura mantém o job fora da varredura: semear para medir escrita não pode
        // alterar a profundidade que a medição de aquisição enxerga.
        State = futuro ? JobState.Scheduled : JobState.Enqueued,
        Queue = Queue,
        CreatedAt = Agora,
        ScheduledFor = futuro ? Agora.AddDays(30) : null,
    };

    private static async Task SemearAsync(IStorage storage, int profundidade, CancellationToken ct)
    {
        for (var i = 0; i < profundidade; i++)
        {
            await storage.Jobs.CreateAsync(NovoJob($"seed-{i:D8}"), ct);
        }
    }

    private static async Task<double> MedirAsync(int amostras, Func<Task> operacao)
    {
        await operacao(); // primeira execução paga abertura de conexão e plano

        var relogio = Stopwatch.StartNew();
        for (var i = 0; i < amostras; i++)
        {
            await operacao();
        }

        return relogio.Elapsed.TotalMicroseconds / amostras;
    }

    /// <summary>
    /// O plano da seleção de candidato, espelhando a consulta de aquisição de cada
    /// provider sem o travamento de linha — a forma do plano é a mesma, e o que se quer
    /// ver é se há varredura sequencial ou ordenação.
    /// </summary>
    private async Task<string> ExplicarAsync(DbConnection connection, string isolamento, CancellationToken ct)
        => storage switch
        {
            StorageKind.PostgreSql => await LerPlanoAsync(connection, ct,
                $"""
                 EXPLAIN (ANALYZE, BUFFERS)
                 SELECT id FROM {isolamento}.jobs
                 WHERE queue = @queue AND eligible_at <= @now
                 ORDER BY eligible_at LIMIT 1
                 """,
                ("queue", Queue), ("now", Agora)),

            StorageKind.MySql => await LerPlanoAsync(connection, ct,
                $"""
                 EXPLAIN ANALYZE
                 SELECT id FROM {isolamento}_jobs
                 WHERE queue = @queue AND eligible_at <= @now
                 ORDER BY eligible_at LIMIT 1
                 """,
                ("queue", Queue), ("now", Agora.UtcDateTime)),

            // O SHOWPLAN do SQL Server é um modo da sessão, não um prefixo: ligado, o
            // servidor devolve o plano estimado em vez de executar a consulta.
            StorageKind.SqlServer => await LerPlanoDoSqlServerAsync(connection, isolamento, ct),

            _ => "(sem plano para este storage)",
        };

    private static async Task<string> LerPlanoDoSqlServerAsync(
        DbConnection connection, string isolamento, CancellationToken ct)
    {
        await ExecutarAsync(connection, "SET SHOWPLAN_TEXT ON", ct);
        try
        {
            return await LerPlanoAsync(connection, ct,
                $"""
                 SELECT TOP (1) id FROM {isolamento}.jobs
                 WHERE queue = @queue AND eligible_at <= @now
                 ORDER BY eligible_at
                 """,
                ("queue", Queue), ("now", Agora));
        }
        finally
        {
            await ExecutarAsync(connection, "SET SHOWPLAN_TEXT OFF", ct);
        }
    }

    private static async Task<string> LerPlanoAsync(
        DbConnection connection, CancellationToken ct, string sql, params (string Nome, object Valor)[] parametros)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (nome, valor) in parametros)
        {
            var parametro = command.CreateParameter();
            parametro.ParameterName = nome;
            parametro.Value = valor;
            command.Parameters.Add(parametro);
        }

        var linhas = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            linhas.Add(reader.GetString(0));
        }

        return string.Join(Environment.NewLine, linhas);
    }

    private static async Task ExecutarAsync(DbConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }
}
