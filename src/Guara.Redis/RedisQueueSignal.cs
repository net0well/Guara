using System.Collections.Concurrent;
using Guara.Abstractions;
using Guara.Core;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Guara.Redis;

/// <summary>
/// Sinal de fila sobre pub/sub do Redis: o aviso emitido num nó chega a todos os outros,
/// que acordam o dispatcher no ato em vez de esperar o próximo ciclo de busca.
/// <para>
/// A espera continua acontecendo num <see cref="InProcessQueueSignal"/> local — a
/// assinatura remota só empurra a mensagem para dentro dele. Isso mantém a retenção do
/// aviso e o despertar num único lugar, e faz o nó que publicou acordar mesmo com o Redis
/// fora do ar.
/// </para>
/// <para>
/// Nada aqui é durável, e não precisa ser: o aviso é best-effort, e o ciclo periódico do
/// dispatcher é o piso que encontra qualquer job cujo aviso se perdeu.
/// </para>
/// </summary>
internal sealed class RedisQueueSignal : IQueueSignal, IDisposable, IAsyncDisposable
{
    private readonly RedisOptions _options;
    private readonly InProcessQueueSignal _local;
    private readonly ILogger<RedisQueueSignal> _logger;
    private readonly Lazy<Task<IConnectionMultiplexer>> _conexao;
    private readonly IConnectionMultiplexer? _conexaoDoContainer;
    private readonly ConcurrentDictionary<string, Task> _assinaturas = new(StringComparer.Ordinal);

    // Guardada assim que a conexão própria abre, para que o encerramento síncrono a
    // alcance sem aguardar uma Task — e sem arriscar iniciar a conexão no Dispose.
    private volatile IConnectionMultiplexer? _conexaoAberta;

    // Identifica as publicações deste nó para que ele descarte o próprio eco: quem
    // publica já avisou o portão local, e reagir de novo custaria uma busca a mais numa
    // fila que acabou de ser drenada.
    private readonly RedisValue _identidade = Guid.NewGuid().ToString("n");

    /// <param name="options">Connection string e prefixo dos canais.</param>
    /// <param name="conexaoDoContainer">Multiplexer da aplicação, quando registrado; nesse
    /// caso o Guará o usa sem assumir a posse dele.</param>
    /// <param name="time">Relógio que mede o teto da espera local.</param>
    /// <param name="logger">Registro das falhas de transporte, que degradam sem derrubar.</param>
    public RedisQueueSignal(
        RedisOptions options,
        IConnectionMultiplexer? conexaoDoContainer,
        TimeProvider time,
        ILogger<RedisQueueSignal> logger)
    {
        _options = options;
        _logger = logger;
        _local = new InProcessQueueSignal(time);
        _conexaoDoContainer = conexaoDoContainer;
        _conexao = conexaoDoContainer is null
            ? new Lazy<Task<IConnectionMultiplexer>>(ConectarAsync, LazyThreadSafetyMode.ExecutionAndPublication)
            : new Lazy<Task<IConnectionMultiplexer>>(Task.FromResult(conexaoDoContainer));
    }

    /// <summary>
    /// A conexão em uso, quando já existe. Nula enquanto ninguém sinalizou nem esperou —
    /// e é por isso que o encerramento não tem o que fazer nesse caso.
    /// </summary>
    private IConnectionMultiplexer? Atual => _conexaoDoContainer ?? _conexaoAberta;

    /// <inheritdoc />
    public async ValueTask SignalAsync(string queue, CancellationToken ct)
    {
        // Portão local primeiro, sem depender da rede: neste nó o dispatcher acorda
        // mesmo que o Redis esteja indisponível.
        _local.Signal(queue);

        try
        {
            var conexao = await _conexao.Value.ConfigureAwait(false);
            await conexao.GetSubscriber().PublishAsync(Canal(queue), _identidade).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or ObjectDisposedException)
        {
            _logger.LogWarning(ex,
                "Não foi possível publicar o aviso da fila {Queue}; ele vale apenas neste nó", queue);
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> WaitAsync(IReadOnlyList<string> queues, TimeSpan timeout, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(queues);

        await GarantirAssinaturasAsync(queues).ConfigureAwait(false);
        return await _local.WaitAsync(queues, timeout, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Atual is not { } conexao)
        {
            return;
        }

        // Canal a canal, nunca UnsubscribeAll: um multiplexer vindo do contêiner pode estar
        // servindo cache e sessão da aplicação, e derrubar tudo o que ele assina seria
        // estragar o que não é nosso. Pelo mesmo motivo, só se fecha o que abrimos.
        foreach (var fila in _assinaturas.Keys)
        {
            try
            {
                await conexao.GetSubscriber().UnsubscribeAsync(Canal(fila)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is RedisException or ObjectDisposedException)
            {
                _logger.LogDebug(ex, "Falha ao cancelar a assinatura da fila {Queue} no encerramento", fila);
            }
        }

        if (_conexaoDoContainer is null)
        {
            await conexao.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Encerramento síncrono. Existe porque um contêiner descartado por
    /// <c>Dispose()</c> recusa serviço que só saiba se encerrar de forma assíncrona — e
    /// hosts fazem exatamente isso.
    /// </summary>
    public void Dispose()
    {
        if (Atual is not { } conexao)
        {
            return;
        }

        foreach (var fila in _assinaturas.Keys)
        {
            try
            {
                conexao.GetSubscriber().Unsubscribe(Canal(fila));
            }
            catch (Exception ex) when (ex is RedisException or ObjectDisposedException)
            {
                _logger.LogDebug(ex, "Falha ao cancelar a assinatura da fila {Queue} no encerramento", fila);
            }
        }

        if (_conexaoDoContainer is null)
        {
            conexao.Dispose();
        }
    }

    private async Task<IConnectionMultiplexer> ConectarAsync()
    {
        var configuracao = ConfigurationOptions.Parse(_options.ConnectionString);

        // Nascer mesmo com o Redis fora e reconectar sozinho depois: o aviso é acessório,
        // e abortar aqui trocaria uma otimização de latência por uma aplicação que não sobe.
        configuracao.AbortOnConnectFail = false;

        var conexao = await ConnectionMultiplexer.ConnectAsync(configuracao).ConfigureAwait(false);
        _conexaoAberta = conexao;
        return conexao;
    }

    private async Task GarantirAssinaturasAsync(IReadOnlyList<string> queues)
    {
        for (var i = 0; i < queues.Count; i++)
        {
            var fila = queues[i];

            // A Task da assinatura fica no dicionário: esperas concorrentes aguardam a
            // mesma, e a assinatura que falhou é removida para a próxima tentar de novo.
            var assinatura = _assinaturas.GetOrAdd(fila, AssinarAsync);
            try
            {
                await assinatura.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _assinaturas.TryRemove(new KeyValuePair<string, Task>(fila, assinatura));
                _logger.LogWarning(ex,
                    "Sem assinatura da fila {Queue}: este nó acorda por aviso local ou pelo ciclo de busca", fila);
            }
        }
    }

    private async Task AssinarAsync(string fila)
    {
        var conexao = await _conexao.Value.ConfigureAwait(false);
        await conexao.GetSubscriber().SubscribeAsync(Canal(fila), (_, valor) =>
        {
            if (valor == _identidade)
            {
                return;
            }

            _local.Signal(fila);
        }).ConfigureAwait(false);
    }

    private RedisChannel Canal(string queue)
        => RedisChannel.Literal($"{_options.ChannelPrefix}:queue:{queue}");
}
