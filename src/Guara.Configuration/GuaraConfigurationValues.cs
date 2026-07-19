using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Guara.Configuration;

/// <summary>
/// Leitores tipados de valores de configuração, com parsing explícito (invariante,
/// zero reflection — AOT garantido). Chave ausente/vazia retorna <c>null</c> (o
/// componente mantém o default); valor presente mas inválido <b>lança</b> com o
/// caminho completo — configuração errada falha no startup, nunca em silêncio.
/// </summary>
public static class GuaraConfigurationValues
{
    /// <summary>Lê um inteiro.</summary>
    /// <param name="section">Seção do componente.</param>
    /// <param name="key">Chave dentro da seção.</param>
    /// <returns>O valor, ou <c>null</c> quando ausente.</returns>
    /// <exception cref="InvalidOperationException">Valor presente que não é um inteiro.</exception>
    public static int? ReadInt32(IConfigurationSection section, string key)
    {
        ArgumentNullException.ThrowIfNull(section);
        var raw = section[key];
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Invalid(section, key, raw, "um inteiro (ex.: 8)");
    }

    /// <summary>Lê um <see cref="TimeSpan"/> no formato invariante (ex.: <c>00:00:15</c>).</summary>
    /// <param name="section">Seção do componente.</param>
    /// <param name="key">Chave dentro da seção.</param>
    /// <returns>O valor, ou <c>null</c> quando ausente.</returns>
    /// <exception cref="InvalidOperationException">Valor presente que não é um intervalo.</exception>
    public static TimeSpan? ReadTimeSpan(IConfigurationSection section, string key)
    {
        ArgumentNullException.ThrowIfNull(section);
        var raw = section[key];
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Invalid(section, key, raw, "um intervalo (ex.: 00:00:15 ou 1.00:00:00)");
    }

    /// <summary>Lê um booleano (<c>true</c>/<c>false</c>).</summary>
    /// <param name="section">Seção do componente.</param>
    /// <param name="key">Chave dentro da seção.</param>
    /// <returns>O valor, ou <c>null</c> quando ausente.</returns>
    /// <exception cref="InvalidOperationException">Valor presente que não é booleano.</exception>
    public static bool? ReadBoolean(IConfigurationSection section, string key)
    {
        ArgumentNullException.ThrowIfNull(section);
        var raw = section[key];
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return bool.TryParse(raw, out var value)
            ? value
            : throw Invalid(section, key, raw, "um booleano (true/false)");
    }

    /// <summary>Lê uma string não vazia.</summary>
    /// <param name="section">Seção do componente.</param>
    /// <param name="key">Chave dentro da seção.</param>
    /// <returns>O valor, ou <c>null</c> quando ausente/vazio.</returns>
    public static string? ReadString(IConfigurationSection section, string key)
    {
        ArgumentNullException.ThrowIfNull(section);
        var raw = section[key];
        return string.IsNullOrEmpty(raw) ? null : raw;
    }

    /// <summary>Lê um array de strings (itens da seção, na ordem declarada).</summary>
    /// <param name="section">Seção do componente.</param>
    /// <param name="key">Chave dentro da seção.</param>
    /// <returns>Os itens não vazios, ou <c>null</c> quando a chave não existe.</returns>
    public static string[]? ReadStringArray(IConfigurationSection section, string key)
    {
        ArgumentNullException.ThrowIfNull(section);
        var child = section.GetSection(key);
        if (!child.Exists())
        {
            return null;
        }

        var values = child.GetChildren()
            .Select(item => item.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
        return values;
    }

    private static InvalidOperationException Invalid(
        IConfigurationSection section, string key, string raw, string expected)
        => new($"Configuração inválida em '{section.Path}:{key}': '{raw}' não é {expected}.");
}
