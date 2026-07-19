using Guara.Abstractions;
using Guara.Scheduler;
using Guara.Storage;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Extensions.DependencyInjection; // extensões neste namespace aparecem no IntelliSense de builder.Services

/// <summary>Extensão única do pacote <c>Guara.Scheduler</c>.</summary>
public static class SchedulerServiceCollectionExtensions
{
    /// <summary>
    /// Liga o scheduler do Guará: cálculo de agendamentos (<see cref="IScheduler"/>,
    /// cron próprio) e a API pública de jobs (<see cref="IGuaraClient"/>).
    /// </summary>
    /// <param name="builder">Builder do Guará.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public static IGuaraBuilder AddGuaraScheduler(this IGuaraBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<ICronParser, GuaraCronParser>();
        builder.Services.TryAddSingleton<IScheduler, GuaraScheduler>();
        builder.Services.TryAddSingleton<RecurrenceCalculator>();
        builder.Services.TryAddSingleton(sp => new ContinuationPromoter(
            sp.GetRequiredService<IStorage>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<ContinuationPromoter>>() ?? NullLogger<ContinuationPromoter>.Instance));
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEventHandler<JobCompleted>, ContinuationOnParentCompleted>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IEventHandler<JobFailed>, ContinuationOnParentFailed>());
        builder.Services.TryAddSingleton<IGuaraClient>(sp => new GuaraClient(
            sp.GetRequiredService<IStorage>(),
            sp.GetRequiredService<IEventPublisher>(),
            sp.GetRequiredService<RecurrenceCalculator>(),
            sp.GetRequiredService<ContinuationPromoter>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<GuaraClient>>() ?? NullLogger<GuaraClient>.Instance));
        return builder;
    }
}
