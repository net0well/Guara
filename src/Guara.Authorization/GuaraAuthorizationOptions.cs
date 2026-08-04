using Guara.Abstractions;

namespace Guara.Authorization;

/// <summary>
/// Configuração das permissões do painel. Nada aqui abre acesso: o que não estiver
/// concedido explicitamente é negado.
/// </summary>
public sealed class GuaraAuthorizationOptions
{
    /// <summary>
    /// Mapeia uma ação (<see cref="GuaraActions"/>) para uma policy do ASP.NET Core.
    /// Ação sem mapeamento cai em <see cref="DefaultPolicy"/> e, na falta dela, exige a
    /// claim <see cref="GuaraClaimTypes.Permission"/> com o nome da ação.
    /// </summary>
    public IDictionary<string, string> ActionPolicies { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Policy aplicada às ações sem mapeamento próprio. Nula por padrão — sem ela, o
    /// critério é a claim de permissão.
    /// </summary>
    public string? DefaultPolicy { get; set; }

    /// <summary>
    /// Papéis com acesso total, avaliados antes de qualquer policy ou claim. Vazio por
    /// padrão: acesso total é concessão explícita, nunca herdada de um nome convencional.
    /// </summary>
    public IList<string> AdminRoles { get; } = [];

    /// <summary>
    /// Concede todas as ações a quem satisfizer a policy informada — atalho para o caso
    /// comum de "um administrador do painel opera tudo".
    /// </summary>
    /// <param name="policy">Nome da policy do ASP.NET Core.</param>
    /// <returns>As próprias opções, para encadeamento fluente.</returns>
    public GuaraAuthorizationOptions AllowAll(string policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);
        foreach (var action in GuaraActions.All)
        {
            ActionPolicies[action] = policy;
        }

        return this;
    }

    /// <summary>Exige uma policy para uma ação específica.</summary>
    /// <param name="action">Ação (ver <see cref="GuaraActions"/>).</param>
    /// <param name="policy">Nome da policy do ASP.NET Core.</param>
    /// <returns>As próprias opções, para encadeamento fluente.</returns>
    public GuaraAuthorizationOptions Require(string action, string policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);
        ActionPolicies[action] = policy;
        return this;
    }

    /// <summary>
    /// Rejeita configuração inválida no boot: um nome de ação errado passaria despercebido
    /// e a ação real continuaria caindo no critério mais restritivo, sem ninguém notar.
    /// </summary>
    internal void Validate()
    {
        foreach (var (action, policy) in ActionPolicies)
        {
            if (!GuaraActions.IsKnown(action))
            {
                throw new InvalidOperationException(
                    $"Ação desconhecida em ActionPolicies: '{action}'. Conhecidas: "
                        + string.Join(", ", GuaraActions.All));
            }

            if (string.IsNullOrWhiteSpace(policy))
            {
                throw new InvalidOperationException($"Policy vazia para a ação '{action}'.");
            }
        }

        if (DefaultPolicy is { Length: 0 })
        {
            throw new InvalidOperationException("DefaultPolicy não pode ser vazia; use nulo para desativá-la.");
        }

        if (AdminRoles.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("AdminRoles não aceita papel vazio.");
        }
    }
}
