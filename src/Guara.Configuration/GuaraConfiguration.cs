using Microsoft.Extensions.Configuration;

namespace Guara.Configuration;

/// <summary>
/// Acesso à configuração do Guará por convenção de seções: a raiz é <c>Guara</c> e
/// cada componente lê <c>Guara:{Componente}</c> (ex.: <c>Guara:Worker:MaxConcurrency</c>).
/// Registrada por <c>UseConfiguration</c>; na ausência dela, cada componente usa os
/// defaults das próprias opções.
/// </summary>
public sealed class GuaraConfiguration(IConfiguration configuration)
{
    /// <summary>Nome da seção raiz de toda configuração do Guará.</summary>
    public const string RootSection = "Guara";

    /// <summary>Seção raiz (<c>Guara</c>) — opções globais moram direto nela.</summary>
    public IConfigurationSection Root => configuration.GetSection(RootSection);

    /// <summary>Seção de um componente (<c>Guara:{name}</c>).</summary>
    /// <param name="name">Nome do componente (ex.: <c>Worker</c>, <c>Server</c>).</param>
    /// <returns>A seção correspondente (pode não existir — leitores retornam <c>null</c>).</returns>
    public IConfigurationSection Component(string name)
        => configuration.GetSection(RootSection).GetSection(name);
}
