using Guara.Abstractions;
using Guara.Dispatcher;
using Guara.Scheduler;
using Guara.Server;
using Guara.Storage;
using Guara.Worker;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

/// <summary>Extensão única do pacote <c>Guara.Server</c>.</summary>
public static class ServerServiceCollectionExtensions
{
    /// <summary>
    /// Liga o servidor completo do Guará: scheduler, executor, worker e dispatcher
    /// (cada um com seus defaults) mais o ciclo de vida — heartbeat, manutenção e o
    /// <see cref="IHostedService"/> que inicia tudo no boot da aplicação.
    /// Para customizar um motor, chame o <c>AddGuara*</c> dele <b>antes</b> deste método
    /// (a primeira configuração registrada vence).
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <param name="configure">Ajuste opcional das opções do servidor.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder AddGuaraServer(this IGuaraBuilder builder, Action<ServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new ServerOptions();
        configure?.Invoke(options);

        builder
            .AddGuaraScheduler()
            .AddGuaraExecutor()
            .AddGuaraWorker()
            .AddGuaraDispatcher();

        builder.Services.TryAddSingleton(options);
        builder.Services.TryAddSingleton<IGuaraServer>(sp => new GuaraServer(
            sp.GetRequiredService<IStorage>(),
            sp.GetRequiredService<IDispatcher>(),
            sp.GetRequiredService<IWorker>(),
            sp.GetRequiredService<IGuaraClient>(),
            sp.GetRequiredService<RecurrenceCalculator>(),
            sp.GetRequiredService<ServerOptions>(),
            sp.GetRequiredService<DispatcherOptions>(),
            sp.GetRequiredService<WorkerOptions>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<GuaraServer>>() ?? NullLogger<GuaraServer>.Instance));
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, GuaraServerHostedService>());

        return builder;
    }
}
