using System.Collections.Concurrent;
using Guara.Abstractions;

namespace Guara.Executor;

/// <summary>
/// Registro de handlers de job (<c>tipo.método → delegate</c>) e dos metadados de
/// execução, usado pelo <see cref="RegistryJobInvoker"/>. O código gerado registra
/// via <see cref="IJobModule"/> (handlers com acesso a serviços); o registro manual
/// continua disponível para bootstrap simples. Leituras são thread-safe.
/// </summary>
public sealed class JobHandlerRegistry : IJobMetadataProvider
{
    private readonly ConcurrentDictionary<string, Func<IServiceProvider, IJobContext, CancellationToken, ValueTask>> _handlers =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, JobExecutionMetadata> _metadata = new(StringComparer.Ordinal);

    /// <summary>Registra o handler de um job (forma simples, sem serviços).</summary>
    /// <param name="typeName">Nome do tipo (igual ao <see cref="JobDescriptor.TypeName"/>).</param>
    /// <param name="methodName">Nome do método (igual ao <see cref="JobDescriptor.MethodName"/>).</param>
    /// <param name="handler">Delegate que executa o job.</param>
    /// <returns>O próprio registro, para encadeamento fluente.</returns>
    public JobHandlerRegistry Register(
        string typeName, string methodName, Func<IJobContext, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Register(typeName, methodName, metadata: null, (_, context, ct) => handler(context, ct));
    }

    /// <summary>Registra o handler de um job com acesso a serviços e metadados de execução.</summary>
    /// <param name="typeName">Nome do tipo (igual ao <see cref="JobDescriptor.TypeName"/>).</param>
    /// <param name="methodName">Nome do método (igual ao <see cref="JobDescriptor.MethodName"/>).</param>
    /// <param name="metadata">Comportamento declarado do job, quando houver.</param>
    /// <param name="handler">Delegate que resolve dependências e executa o job.</param>
    /// <returns>O próprio registro, para encadeamento fluente.</returns>
    public JobHandlerRegistry Register(
        string typeName, string methodName, JobExecutionMetadata? metadata,
        Func<IServiceProvider, IJobContext, CancellationToken, ValueTask> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodName);
        ArgumentNullException.ThrowIfNull(handler);

        var key = Key(typeName, methodName);
        _handlers[key] = handler;
        if (metadata is not null)
        {
            _metadata[key] = metadata;
        }

        return this;
    }

    /// <inheritdoc />
    public JobExecutionMetadata? GetMetadata(string typeName, string methodName)
        => _metadata.TryGetValue(Key(typeName, methodName), out var metadata) ? metadata : null;

    internal bool TryGet(
        string typeName, string methodName,
        out Func<IServiceProvider, IJobContext, CancellationToken, ValueTask> handler)
        => _handlers.TryGetValue(Key(typeName, methodName), out handler!);

    private static string Key(string typeName, string methodName) => $"{typeName}.{methodName}";
}

/// <summary>
/// <see cref="IJobInvoker"/> baseado no <see cref="JobHandlerRegistry"/> — sem reflection.
/// Job sem handler registrado falha explicitamente (vira <c>Failed</c> com motivo).
/// </summary>
internal sealed class RegistryJobInvoker(JobHandlerRegistry registry, IServiceProvider services) : IJobInvoker
{
    /// <inheritdoc />
    public ValueTask InvokeAsync(IJobContext context, CancellationToken ct)
    {
        var descriptor = context.Descriptor;
        if (!registry.TryGet(descriptor.TypeName, descriptor.MethodName, out var handler))
        {
            throw new InvalidOperationException(
                $"Nenhum handler registrado para o job '{descriptor.TypeName}.{descriptor.MethodName}'. " +
                "Marque o método com [GuaraJob] e chame AddGuaraJobs(), ou registre-o no JobHandlerRegistry.");
        }

        return handler(services, context, ct);
    }
}
