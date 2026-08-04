using System.Security.Claims;

namespace Guara.Abstractions;

/// <summary>
/// Ações do painel sujeitas a permissão. São identificadores estáveis: aparecem em
/// configuração, em claims e em logs, então mudá-los quebra instalações existentes.
/// </summary>
public static class GuaraActions
{
    /// <summary>Consultar contadores, filas, jobs, servidores e recorrentes.</summary>
    public const string View = "guara:view";

    /// <summary>Ver o payload de argumentos de um job (pode conter dado sensível).</summary>
    public const string ViewPayload = "guara:view-payload";

    /// <summary>Reenfileirar um job que falhou definitivamente.</summary>
    public const string Retry = "guara:retry";

    /// <summary>Antecipar um job agendado ou em retentativa, e disparar recorrentes.</summary>
    public const string Trigger = "guara:trigger";

    /// <summary>Excluir jobs.</summary>
    public const string Delete = "guara:delete";

    /// <summary>Criar, editar e excluir calendários de exclusão.</summary>
    public const string Calendars = "guara:calendars";

    /// <summary>Todas as ações conhecidas — usado para validar configuração e emitir claims.</summary>
    public static IReadOnlyList<string> All { get; } =
        [View, ViewPayload, Retry, Trigger, Delete, Calendars];

    /// <summary>Indica se o nome corresponde a uma ação conhecida.</summary>
    /// <param name="action">Nome da ação.</param>
    /// <returns><c>true</c> quando a ação existe.</returns>
    public static bool IsKnown(string action) => All.Contains(action, StringComparer.Ordinal);
}

/// <summary>Tipos de claim que o Guará entende.</summary>
public static class GuaraClaimTypes
{
    /// <summary>
    /// Claim de permissão: o valor é o nome da ação (ver <see cref="GuaraActions"/>).
    /// O host emite uma claim por ação concedida.
    /// </summary>
    public const string Permission = "guara:permission";
}

/// <summary>
/// Decide se uma identidade pode executar uma ação do painel. Contrato puro — a
/// implementação (políticas do ASP.NET Core) vive em <c>Guara.Authorization</c>, e o
/// painel depende apenas desta interface.
/// </summary>
public interface IGuaraAuthorizer
{
    /// <summary>Avalia uma ação para a identidade informada.</summary>
    /// <param name="user">Identidade do chamador.</param>
    /// <param name="action">Ação pretendida (ver <see cref="GuaraActions"/>).</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns><c>true</c> se a ação é permitida; nega por omissão.</returns>
    ValueTask<bool> AuthorizeAsync(ClaimsPrincipal user, string action, CancellationToken ct = default);
}
