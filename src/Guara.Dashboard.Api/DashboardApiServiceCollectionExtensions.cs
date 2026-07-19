using Guara.Abstractions;
using Guara.Dashboard.Api;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

/// <summary>Extensão única do pacote <c>Guara.Dashboard.Api</c>.</summary>
public static class DashboardApiServiceCollectionExtensions
{
    /// <summary>
    /// Liga os serviços da API do dashboard: a ponte de eventos para o stream SSE e a
    /// serialização AOT dos DTOs. O mapeamento das rotas é feito com
    /// <c>app.MapGuaraDashboardApi()</c> (ou pela composição <c>Guara.Dashboard</c>).
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder AddGuaraDashboardApi(this IGuaraBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<DashboardEventStream>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEventHandler<JobCreated>, JobCreatedStreamForwarder>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEventHandler<JobScheduled>, JobScheduledStreamForwarder>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEventHandler<JobCompleted>, JobCompletedStreamForwarder>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEventHandler<JobFailed>, JobFailedStreamForwarder>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEventHandler<JobRetryScheduled>, JobRetryScheduledStreamForwarder>());

        builder.Services.Configure<JsonOptions>(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, DashboardJsonContext.Default));

        return builder;
    }
}
