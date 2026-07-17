using System.Collections.Concurrent;
using Guara.Abstractions;

namespace Guara.Executor;

/// <summary>
/// Registro manual de handlers de job (<c>tipo.método → delegate</c>), usado pelo
/// <see cref="RegistryJobInvoker"/>. É infraestrutura <b>temporária</b>: o
/// <c>Guara.SourceGenerators</c> (spec 029) passará a gerar o registro em compilação.
/// Registre durante o bootstrap; leituras posteriores são thread-safe.
/// </summary>
public sealed class JobHandlerRegistry
{
    private readonly ConcurrentDictionary<string, Func<IJobContext, CancellationToken, ValueTask>> _handlers =
        new(StringComparer.Ordinal);

    /// <summary>Registra o handler de um job.</summary>
    /// <param name="typeName">Nome do tipo (igual ao <see cref="JobDescriptor.TypeName"/>).</param>
    /// <param name="methodName">Nome do método (igual ao <see cref="JobDescriptor.MethodName"/>).</param>
    /// <param name="handler">Delegate que executa o job.</param>
    /// <returns>O próprio registro, para encadeamento fluente.</returns>
    public JobHandlerRegistry Register(
        string typeName, string methodName, Func<IJobContext, CancellationToken, ValueTask> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentNullException.ThrowIfNull(handler);

        _handlers[Key(typeName, methodName)] = handler;
        return this;
    }

    internal bool TryGet(
        string typeName, string methodName,
        out Func<IJobContext, CancellationToken, ValueTask> handler)
        => _handlers.TryGetValue(Key(typeName, methodName), out handler!);

    private static string Key(string typeName, string methodName) => $"{typeName}.{methodName}";
}

/// <summary>
/// <see cref="IJobInvoker"/> baseado no <see cref="JobHandlerRegistry"/> — sem reflection.
/// Job sem handler registrado falha explicitamente (vira <c>Failed</c> com motivo).
/// </summary>
public sealed class RegistryJobInvoker(JobHandlerRegistry registry) : IJobInvoker
{
    /// <inheritdoc />
    public ValueTask InvokeAsync(IJobContext context, CancellationToken ct)
    {
        var descriptor = context.Descriptor;
        if (!registry.TryGet(descriptor.TypeName, descriptor.MethodName, out var handler))
        {
            throw new InvalidOperationException(
                $"Nenhum handler registrado para o job '{descriptor.TypeName}.{descriptor.MethodName}'. " +
                "Registre-o no JobHandlerRegistry (o registro automático chega com o Guara.SourceGenerators).");
        }

        return handler(context, ct);
    }
}
