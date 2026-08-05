using Guara.Abstractions;

namespace Guara.Core;

/// <summary>
/// Sinal de fila <b>em processo</b> — o padrão do Guará, que resolve o nó único sem
/// nenhuma infraestrutura externa.
/// <para>
/// É público porque transportes distribuídos o reaproveitam como portão local: a
/// assinatura remota entrega a mensagem em <see cref="Signal"/> e quem aguarda continua
/// bloqueado num único lugar, sem duplicar a lógica de retenção e despertar.
/// </para>
/// </summary>
/// <param name="time">Relógio que mede o teto da espera.</param>
public sealed class InProcessQueueSignal(TimeProvider time) : IQueueSignal
{
    // Nomes de fila vêm do job, então um padrão dinâmico faria o conjunto retido crescer
    // sem limite. Passado o teto o aviso é descartado, e a fila volta a depender do ciclo
    // periódico de busca — que é o piso de qualquer forma.
    private const int MaxFilasRetidas = 1024;

    private readonly object _portao = new();
    private readonly HashSet<string> _retidos = new(StringComparer.Ordinal);
    private readonly List<Espera> _esperas = [];

    /// <inheritdoc />
    public ValueTask SignalAsync(string queue, CancellationToken ct)
    {
        Signal(queue);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Emite o aviso de forma síncrona. É o ponto de entrada dos transportes externos,
    /// que recebem a mensagem em callback e não têm onde aguardar uma
    /// <see cref="ValueTask"/>.
    /// </summary>
    /// <param name="queue">Fila que recebeu trabalho.</param>
    public void Signal(string queue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);

        List<Espera>? acordadas = null;
        lock (_portao)
        {
            for (var i = _esperas.Count - 1; i >= 0; i--)
            {
                var espera = _esperas[i];
                if (!espera.Interessa(queue))
                {
                    continue;
                }

                // Marcar sob o portão fecha a corrida com quem está desistindo por
                // timeout: a desistência confere esta marca antes de concluir que perdeu.
                espera.Avisada = true;
                (acordadas ??= []).Add(espera);
                _esperas.RemoveAt(i);
            }

            // Ninguém aguardava: retém o aviso para satisfazer a próxima espera.
            if (acordadas is null && _retidos.Count < MaxFilasRetidas)
            {
                _retidos.Add(queue);
            }
        }

        if (acordadas is null)
        {
            return;
        }

        // Fora do portão: liberar quem aguarda não deve acontecer com o lock na mão.
        foreach (var espera in acordadas)
        {
            espera.Concluir();
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> WaitAsync(IReadOnlyList<string> queues, TimeSpan timeout, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(queues);

        Espera nova;
        lock (_portao)
        {
            if (ConsumirRetido(queues))
            {
                return true;
            }

            nova = new Espera(queues);
            _esperas.Add(nova);
        }

        // O teto vive num CTS próprio para que o timer morra junto com a espera: sem
        // isso, cada aviso rápido deixaria um Task.Delay pendente até vencer sozinho.
        using var teto = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var vencimento = Task.Delay(timeout, time, teto.Token);
        await Task.WhenAny(nova.Concluida, vencimento).ConfigureAwait(false);

        bool avisada;
        lock (_portao)
        {
            _esperas.Remove(nova);
            avisada = nova.Avisada;
        }

        if (avisada)
        {
            teto.Cancel();
            return true;
        }

        ct.ThrowIfCancellationRequested();
        return false;
    }

    private bool ConsumirRetido(IReadOnlyList<string> queues)
    {
        var achou = false;
        for (var i = 0; i < queues.Count; i++)
        {
            achou |= _retidos.Remove(queues[i]);
        }

        return achou;
    }

    private sealed class Espera(IReadOnlyList<string> queues)
    {
        private readonly TaskCompletionSource _origem = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Escrita e lida sob o portão do sinal.</summary>
        public bool Avisada { get; set; }

        public Task Concluida => _origem.Task;

        public bool Interessa(string queue)
        {
            for (var i = 0; i < queues.Count; i++)
            {
                if (string.Equals(queues[i], queue, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public void Concluir() => _origem.TrySetResult();
    }
}
