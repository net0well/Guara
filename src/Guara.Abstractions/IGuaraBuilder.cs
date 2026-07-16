using Microsoft.Extensions.DependencyInjection;

namespace Guara.Abstractions;

/// <summary>
/// Raiz da API fluente de configuração do Guará. Cada pacote expõe uma única
/// extensão <c>AddGuara...()</c>/<c>Use...()</c> que recebe e devolve este builder.
/// </summary>
public interface IGuaraBuilder
{
    /// <summary>Coleção de serviços da aplicação.</summary>
    IServiceCollection Services { get; }
}
