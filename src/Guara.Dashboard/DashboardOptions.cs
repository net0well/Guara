namespace Guara.Dashboard;

/// <summary>
/// Opções do dashboard. Configuráveis pela seção <c>Guara:Dashboard</c>; as regras de
/// acesso entram por código via <see cref="UseGuaraAuthentication"/>.
/// </summary>
public sealed class DashboardOptions
{
    /// <summary>Rota base do dashboard (UI + API).</summary>
    public string BasePath { get; set; } = "/guara";

    /// <summary>
    /// Exige autorização em tudo (default). Só desligue em ambiente fechado — o
    /// startup registra um aviso forte quando desligado.
    /// </summary>
    public bool RequireAuthorization { get; set; } = true;

    /// <summary>
    /// Segredo do cookie de sessão do login fixo. Nulo gera uma chave aleatória por
    /// boot (reiniciar o processo derruba as sessões); defina para sobreviver a
    /// restarts e para múltiplos nós atrás de load balancer.
    /// </summary>
    public string? CookieSecret { get; set; }

    /// <summary>Validade da sessão do login fixo.</summary>
    public TimeSpan SessionTtl { get; set; } = TimeSpan.FromHours(8);

    internal DashboardAccessOptions? Access { get; private set; }

    /// <summary>
    /// Configura as regras de acesso do dashboard (combinadas em E; use
    /// <c>QualquerUma</c> para OU). Sem esta chamada, o dashboard exige apenas um
    /// usuário autenticado pelo host.
    /// </summary>
    /// <param name="configure">Configuração fluente das regras.</param>
    /// <returns>As próprias opções, para encadeamento fluente.</returns>
    public DashboardOptions UseGuaraAuthentication(Action<DashboardAccessBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new DashboardAccessBuilder();
        configure(builder);
        Access = builder.Build();
        return this;
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(BasePath) || !BasePath.StartsWith('/') || BasePath.Length > 1 && BasePath.EndsWith('/'))
        {
            throw new InvalidOperationException(
                $"DashboardOptions.BasePath inválido: '{BasePath}'. Use um caminho como \"/guara\" " +
                "(inicia com '/', sem barra final).");
        }

        if (SessionTtl <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"DashboardOptions.SessionTtl precisa ser positivo (recebido: {SessionTtl}).");
        }
    }
}
