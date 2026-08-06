using BenchmarkDotNet.Attributes;
using Guara.Abstractions;

namespace Guara.Storage.Memory.Benchmarks;

/// <summary>
/// Custo das duas operações que todo job atravessa: entrar na fila e ser adquirido.
/// <para>
/// O provider in-memory é o piso — ele não tem rede nem disco, então o que aparece aqui é
/// o que o Guará gasta por conta própria. Um provider de banco soma o custo dele em cima
/// disto; comparar os dois mostra quanto do tempo é framework e quanto é infraestrutura.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class JobStorageBenchmarks
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private MemoryStorage _storage = null!;
    private int _sequencia;

    /// <summary>Quantos jobs a fila já tem quando a aquisição roda.</summary>
    [Params(1, 1_000, 50_000)]
    public int Profundidade { get; set; }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static JobRecord NewJob(string id) => new()
    {
        Id = new JobId(id),
        Descriptor = new JobDescriptor("Relatorios.Mensal", "GerarAsync", default, "default"),
        State = JobState.Enqueued,
        Queue = "default",
        CreatedAt = T0,
    };

    [GlobalSetup]
    public async Task Setup()
    {
        _storage = new MemoryStorage(new FixedTimeProvider(T0));
        for (var i = 0; i < Profundidade; i++)
        {
            await _storage.Jobs.CreateAsync(NewJob($"seed-{i}"), CancellationToken.None);
        }
    }

    [Benchmark(Description = "CreateAsync — entrada na fila")]
    public async ValueTask<JobId> Create()
        => await _storage.Jobs.CreateAsync(NewJob($"n-{Interlocked.Increment(ref _sequencia)}"), CancellationToken.None);

    /// <summary>
    /// A aquisição precisa escolher o mais antigo elegível. Medir com a fila em várias
    /// profundidades mostra se esse custo é constante ou cresce com o acúmulo — que é o
    /// cenário real de quem tem backlog.
    /// <para>
    /// O relógio avança um ano de propósito: adquirir marca o job como em execução, e sem
    /// isso a fila drenaria nas primeiras invocações e o benchmark passaria a medir
    /// varredura vazia. Com o lease vencido, cada job volta a ser elegível e a
    /// profundidade se mantém constante do começo ao fim.
    /// </para>
    /// </summary>
    [Benchmark(Description = "AcquireNextDueAsync — aquisição atômica com lease, um job")]
    public async ValueTask<IReadOnlyList<JobRecord>> AcquireNextDue()
        => await _storage.Jobs.AcquireNextDueAsync("default", 1, Lease, T0.AddYears(1), CancellationToken.None);

    /// <summary>
    /// O mesmo trabalho com lote: mostra quanto do custo é por job e quanto é por chamada.
    /// No provider in-memory não há ida-e-volta a amortizar, então a diferença aqui é o
    /// piso da comparação com os providers persistentes.
    /// </summary>
    [Benchmark(Description = "AcquireNextDueAsync — lote de 16")]
    public async ValueTask<IReadOnlyList<JobRecord>> AcquireBatch()
        => await _storage.Jobs.AcquireNextDueAsync("default", 16, Lease, T0.AddYears(1), CancellationToken.None);

    [Benchmark(Description = "GetAsync — leitura por id")]
    public async ValueTask<JobRecord?> Get()
        => await _storage.Jobs.GetAsync(new JobId("seed-0"), CancellationToken.None);
}
