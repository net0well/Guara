using Guara.Abstractions;
using Guara.Storage;
using Microsoft.Extensions.Logging;

namespace Guara.Cluster;

/// <summary>
/// Eleição de líder sobre o lock distribuído do storage. O lock decide quem lidera; a
/// renovação em segundo plano é o que mantém a liderança viva enquanto o trabalho
/// acontece.
/// <para>
/// Sem a renovação, um papel cujo trabalho demore mais que a validade do lock perderia a
/// posse no meio do caminho e outro nó começaria o mesmo ciclo — que é a falha que este
/// componente existe para fechar.
/// </para>
/// </summary>
internal sealed class LockLeaderElection(
    IStorage storage,
    ClusterOptions options,
    TimeProvider time,
    ILogger<LockLeaderElection> logger) : ILeaderElection
{
    /// <inheritdoc />
    public async ValueTask<ILeadership?> TryAcquireAsync(string role, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        var handle = await storage.Locks.TryAcquireAsync(role, options.LeadershipTtl, ct);
        return handle is null ? null : new Leadership(handle, role, options, time, logger);
    }

    private sealed class Leadership : ILeadership
    {
        private readonly ILockHandle _handle;
        private readonly CancellationTokenSource _lost = new();
        private readonly CancellationTokenSource _parar = new();
        private readonly Task _renovacao;

        public Leadership(
            ILockHandle handle, string role, ClusterOptions options,
            TimeProvider time, ILogger logger)
        {
            _handle = handle;
            Role = role;
            _renovacao = Task.Run(
                () => RenovarAsync(handle, role, options, time, logger, _lost, _parar.Token),
                CancellationToken.None);
        }

        public string Role { get; }

        public CancellationToken Lost => _lost.Token;

        public async ValueTask DisposeAsync()
        {
            await _parar.CancelAsync();
            await _renovacao;

            // Liberar antes de descartar devolve o papel para disputa na hora, em vez de
            // deixar os outros nós esperando a validade vencer.
            await _handle.DisposeAsync();

            _parar.Dispose();
            _lost.Dispose();
        }

        private static async Task RenovarAsync(
            ILockHandle handle, string role, ClusterOptions options, TimeProvider time,
            ILogger logger, CancellationTokenSource lost, CancellationToken parar)
        {
            try
            {
                while (!parar.IsCancellationRequested)
                {
                    await Task.Delay(options.RenewInterval, time, parar);

                    if (!await handle.RenewAsync(options.LeadershipTtl, parar))
                    {
                        // Posse perdida: avisar é obrigatório, porque quem lidera precisa
                        // parar o trabalho em vez de seguir agindo como líder.
                        logger.LogWarning(
                            "Liderança de {Role} perdida na renovação; o trabalho em curso será cancelado", role);
                        await lost.CancelAsync();
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (parar.IsCancellationRequested)
            {
                // Descarte normal: quem liderava está devolvendo o papel.
            }
            catch (Exception ex)
            {
                // Falha ao falar com o storage é indistinguível de posse perdida do ponto
                // de vista de quem depende dela: ceder é a escolha segura.
                logger.LogWarning(ex,
                    "Falha ao renovar a liderança de {Role}; cedendo o papel", role);
                await lost.CancelAsync();
            }
        }
    }
}
