using BenchmarkDotNet.Attributes;
using Guara.Abstractions;

namespace Guara.Serialization.Benchmarks;

/// <summary>
/// Custo de (de)serializar os argumentos de um job — pago uma vez ao enfileirar e uma vez
/// ao executar, então entra duas vezes no custo por job.
/// <para>
/// O que se quer ver aqui: se o caminho source-gen se mantém sem reflection e com
/// alocação proporcional ao payload, e não à quantidade de tipos registrados.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class SerializerBenchmarks
{
    private SystemTextJsonSerializer _serializer = null!;

    private object?[] _argumentosSimples = null!;
    private object?[] _argumentosLargos = null!;

    private byte[] _payloadSimples = null!;
    private byte[] _payloadLargo = null!;
    private byte[] _payloadDescriptor = null!;

    private JobDescriptor _descriptor = null!;

    [GlobalSetup]
    public void Setup()
    {
        _serializer = new SystemTextJsonSerializer(SerializerTypeRegistry.CreateDefault());

        // O caso comum: identificador e um punhado de escalares.
        _argumentosSimples = [42, "cliente-1234", true];

        // O caso ruim plausível: texto grande e vários argumentos, para separar o custo
        // fixo do envelope do custo proporcional ao conteúdo.
        _argumentosLargos =
        [
            new string('x', 4096),
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            123456789L,
            3.14159,
            decimal.MaxValue,
            TimeSpan.FromHours(3),
            false,
        ];

        _descriptor = new JobDescriptor("Relatorios.Mensal", "GerarAsync", default, "relatorios");

        _payloadSimples = _serializer.SerializeArgs(_argumentosSimples).ToArray();
        _payloadLargo = _serializer.SerializeArgs(_argumentosLargos).ToArray();
        _payloadDescriptor = _serializer.Serialize(_descriptor).ToArray();
    }

    [Benchmark(Description = "SerializeArgs — 3 escalares")]
    public ReadOnlyMemory<byte> SerializeArgsSimples() => _serializer.SerializeArgs(_argumentosSimples);

    [Benchmark(Description = "SerializeArgs — 8 argumentos, texto de 4 KB")]
    public ReadOnlyMemory<byte> SerializeArgsLargos() => _serializer.SerializeArgs(_argumentosLargos);

    [Benchmark(Description = "DeserializeArgs — 3 escalares")]
    public object?[] DeserializeArgsSimples() => _serializer.DeserializeArgs(_payloadSimples);

    [Benchmark(Description = "DeserializeArgs — 8 argumentos, texto de 4 KB")]
    public object?[] DeserializeArgsLargos() => _serializer.DeserializeArgs(_payloadLargo);

    [Benchmark(Description = "Serialize<JobDescriptor> — tipo estático conhecido")]
    public ReadOnlyMemory<byte> SerializeDescriptor() => _serializer.Serialize(_descriptor);

    [Benchmark(Description = "Deserialize<JobDescriptor> — tipo estático conhecido")]
    public JobDescriptor? DeserializeDescriptor() => _serializer.Deserialize<JobDescriptor>(_payloadDescriptor);
}
