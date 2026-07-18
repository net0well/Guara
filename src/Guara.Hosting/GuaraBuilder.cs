using Guara.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Guara.Hosting;

/// <summary>Implementação do builder fluente devolvido por <c>AddGuara()</c>.</summary>
internal sealed class GuaraBuilder(IServiceCollection services) : IGuaraBuilder
{
    public IServiceCollection Services { get; } = services;
}
