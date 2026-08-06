namespace Guara.Abstractions;

/// <summary>
/// Quantos jobs o worker consegue receber agora. Existe para o dispatcher dimensionar a
/// aquisição em lote: ele pede ao storage o que couber nos slots livres, e nunca mais que
/// isso.
/// <para>
/// É contrato próprio, e não um membro de <see cref="IWorker"/>, para que o dispatcher
/// enxergue a capacidade sem ganhar junto o poder de ligar e desligar o worker.
/// </para>
/// </summary>
public interface IWorkerCapacity
{
    /// <summary>
    /// Vagas livres neste instante. É uma leitura instantânea de estado concorrente: o
    /// valor pode mudar antes de o chamador usá-lo, e o backpressure do canal continua
    /// sendo a garantia de que ninguém busca além da capacidade real.
    /// </summary>
    int Available { get; }
}
