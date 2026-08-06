namespace Guara.Cluster;

/// <summary>Opções da coordenação entre nós. Configuráveis pela seção <c>Guara:Cluster</c>.</summary>
public sealed class ClusterOptions
{
    /// <summary>
    /// Validade da liderança a cada renovação. É também quanto tempo o papel fica preso
    /// se o líder morrer sem se despedir: nenhum outro nó assume antes disso.
    /// </summary>
    public TimeSpan LeadershipTtl { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Intervalo entre renovações. Precisa ser confortavelmente menor que
    /// <see cref="LeadershipTtl"/>: é a folga que permite uma renovação falhar por
    /// intermitência sem que a liderança caia junto.
    /// </summary>
    public TimeSpan RenewInterval { get; set; } = TimeSpan.FromSeconds(10);

    internal void Validate()
    {
        if (LeadershipTtl <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"ClusterOptions.LeadershipTtl precisa ser positivo (recebido: {LeadershipTtl}).");
        }

        if (RenewInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"ClusterOptions.RenewInterval precisa ser positivo (recebido: {RenewInterval}).");
        }

        // Renovar no mesmo ritmo do vencimento não deixa folga para uma tentativa falhar:
        // a primeira intermitência derrubaria a liderança.
        if (RenewInterval >= LeadershipTtl)
        {
            throw new InvalidOperationException(
                $"ClusterOptions.RenewInterval ({RenewInterval}) precisa ser menor que " +
                $"LeadershipTtl ({LeadershipTtl}).");
        }
    }
}
