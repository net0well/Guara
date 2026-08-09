namespace Guara.Core;

/// <summary>
/// Política de retentativa após falha. <see cref="MaxAttempts"/> e
/// <see cref="InProcessAttempts"/> são configuráveis pela seção <c>Guara:Retry</c>;
/// <see cref="Backoff"/> só por código.
/// </summary>
public sealed class RetryOptions
{
    /// <summary>
    /// Número máximo de retentativas <b>persistentes</b> após a primeira falha: o job é
    /// reagendado no storage como <c>Retrying</c> e reexecutado depois. <c>3</c> por padrão
    /// (jobs com efeito colateral irreversível devem usar <c>0</c>).
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Retentativas <b>em processo</b> antes de a falha virar uma tentativa persistente —
    /// <c>0</c> (desligado) por padrão.
    /// <para>
    /// Existe para a oscilação de milissegundos que a retentativa persistente atende caro:
    /// falhar e reagendar custa uma escrita, uma reaquisição e uma releitura no storage, o
    /// que é desproporcional para um deadlock de banco ou um socket que cai. Repetir na
    /// hora resolve com zero ida ao storage.
    /// </para>
    /// <para>
    /// O custo é que o job <b>segura a vaga do worker</b> durante o back-off, então valores
    /// altos trocam vazão por resiliência. Mantenha baixo (1 ou 2) e só onde a falha
    /// esperada é mesmo transitória: não é substituto da retentativa persistente, que é a
    /// única que sobrevive à queda do nó.
    /// </para>
    /// </summary>
    public int InProcessAttempts { get; set; }

    /// <summary>
    /// Back-off para a retentativa de índice <c>attempt</c> (0 = primeira retentativa).
    /// Padrão: exponencial 2^attempt segundos.
    /// <para>
    /// Vale para os dois modos, e é o motivo de <see cref="InProcessAttempts"/> nascer
    /// desligado: o padrão exponencial em segundos seguraria a vaga do worker por tempo
    /// demais. Quem liga a retentativa em processo normalmente encurta o back-off junto.
    /// </para>
    /// </summary>
    public Func<int, TimeSpan> Backoff { get; set; } =
        static attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt));
}
