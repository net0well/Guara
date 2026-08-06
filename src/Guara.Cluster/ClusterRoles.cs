namespace Guara.Cluster;

/// <summary>
/// Os papéis que o próprio Guará disputa entre nós. Nomeados aqui, e não repetidos como
/// literal em cada componente, para que o painel possa perguntar por eles sem adivinhar.
/// </summary>
public static class ClusterRoles
{
    /// <summary>Promoção de ocorrências de jobs recorrentes.</summary>
    public const string Recurring = "guara:recurring";

    /// <summary>Varredura de continuações, limpeza de nós mortos e purga por retenção.</summary>
    public const string Maintenance = "guara:maintenance";

    /// <summary>Todos os papéis do framework, na ordem em que o painel os exibe.</summary>
    public static IReadOnlyList<string> Todos { get; } = [Recurring, Maintenance];
}
