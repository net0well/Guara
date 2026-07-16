using Guara.Abstractions;
using Guara.Storage;
using Guara.Storage.Memory;

namespace Microsoft.Extensions.DependencyInjection; // namespace obrigatório (ADR-0006)

/// <summary>Extensão única do pacote <c>Guara.Storage.Memory</c>.</summary>
public static class MemoryStorageExtensions
{
    /// <summary>
    /// Seleciona o storage in-memory (desenvolvimento/testes/demos — não durável).
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder UseMemoryStorage(this IGuaraBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IStorage>(
            sp => new MemoryStorage(sp.GetService<TimeProvider>() ?? TimeProvider.System));
        return builder;
    }
}
