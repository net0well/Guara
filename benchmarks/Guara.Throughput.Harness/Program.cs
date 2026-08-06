using System.Globalization;
using Guara.Throughput.Harness;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(HarnessOptions.Ajuda);
    return 0;
}

HarnessOptions options;
try
{
    options = HarnessOptions.Parse(args);
}
catch (Exception ex) when (ex is ArgumentException or FormatException)
{
    Console.Error.WriteLine($"Argumento inválido: {ex.Message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(HarnessOptions.Ajuda);
    return 1;
}

using var cancelamento = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancelamento.Cancel();
};

await using var infraestrutura = await Infraestrutura.SubirAsync(options.Storage, cancelamento.Token);

Console.WriteLine($"Máquina      : {Environment.ProcessorCount} núcleos lógicos, .NET {Environment.Version}");
Console.WriteLine($"Storage      : {options.Storage}");

if (options.Mode == HarnessMode.Probe)
{
    Console.WriteLine($"Modo         : decomposição do custo de aquisição");
    Console.WriteLine($"Amostras     : {options.Jobs:N0} por medição");
    Console.WriteLine();

    await new StorageProbe(infraestrutura.ConnectionString!)
        .RunAsync(options.Concurrencies, options.Jobs, cancelamento.Token);
    return 0;
}

Console.WriteLine($"Jobs/rodada  : {options.Jobs:N0}");
Console.WriteLine($"Concorrência : {string.Join(", ", options.Concurrencies)}");
Console.WriteLine();

var harness = new ThroughputHarness(options, infraestrutura.ConnectionString);
var resultados = new List<RunResult>();

foreach (var concorrencia in options.Concurrencies)
{
    Console.Write($"  rodando concorrência {concorrencia}... ");
    var resultado = await harness.RunAsync(concorrencia, cancelamento.Token);
    resultados.Add(resultado);
    Console.WriteLine($"{resultado.EndToEndPerSecond:N0} jobs/s");
}

Console.WriteLine();
Imprimir(resultados);
return 0;

static void Imprimir(List<RunResult> resultados)
{
    var cultura = CultureInfo.InvariantCulture;

    Console.WriteLine("| Concorrência | Enfileiramento (jobs/s) | Execução (jobs/s) | Latência p50 | p95 | p99 | Alocado/job |");
    Console.WriteLine("|---:|---:|---:|---:|---:|---:|---:|");

    foreach (var r in resultados)
    {
        Console.WriteLine(string.Create(cultura,
            $"| {r.Concurrency} " +
            $"| {r.EnqueuePerSecond:N0} " +
            $"| {r.EndToEndPerSecond:N0} " +
            $"| {r.LatencyP50Ms:N1} ms " +
            $"| {r.LatencyP95Ms:N1} ms " +
            $"| {r.LatencyP99Ms:N1} ms " +
            $"| {r.AllocatedPerJob:N0} B |"));
    }

    if (resultados.Count < 2)
    {
        return;
    }

    // O que se quer enxergar: se a execução para de crescer com a concorrência, o limite
    // não está no worker — está em quem alimenta o worker.
    var primeiro = resultados[0];
    var ultimo = resultados[^1];
    var ganho = ultimo.EndToEndPerSecond / primeiro.EndToEndPerSecond;
    var fator = (double)ultimo.Concurrency / primeiro.Concurrency;

    Console.WriteLine();
    Console.WriteLine(string.Create(cultura,
        $"Concorrência {fator:N0}× maior rendeu {ganho:N2}× de vazão."));
    Console.WriteLine(ganho < fator / 2
        ? "A vazão não acompanhou a concorrência: o gargalo está antes do worker."
        : "A vazão acompanhou a concorrência dentro do esperado.");
}

/// <summary>Container de banco da rodada, quando o storage exige um.</summary>
file sealed class Infraestrutura : IAsyncDisposable
{
    private readonly Func<ValueTask> _descartar;

    private Infraestrutura(string? connectionString, Func<ValueTask> descartar)
    {
        ConnectionString = connectionString;
        _descartar = descartar;
    }

    public string? ConnectionString { get; }

    public static async Task<Infraestrutura> SubirAsync(StorageKind storage, CancellationToken ct)
    {
        switch (storage)
        {
            case StorageKind.Memory:
                return new Infraestrutura(null, () => ValueTask.CompletedTask);

            case StorageKind.PostgreSql:
            {
                var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
                await container.StartAsync(ct);
                return new Infraestrutura(container.GetConnectionString(), container.DisposeAsync);
            }

            case StorageKind.SqlServer:
            {
                var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
                await container.StartAsync(ct);
                return new Infraestrutura(container.GetConnectionString(), container.DisposeAsync);
            }

            case StorageKind.MySql:
            {
                var container = new MySqlBuilder("mysql:8.4").Build();
                await container.StartAsync(ct);
                return new Infraestrutura(container.GetConnectionString(), container.DisposeAsync);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(storage), storage, "Storage desconhecido.");
        }
    }

    public ValueTask DisposeAsync() => _descartar();
}
