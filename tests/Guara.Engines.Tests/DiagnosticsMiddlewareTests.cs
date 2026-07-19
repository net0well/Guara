using System.Diagnostics;
using System.Diagnostics.Metrics;
using Guara.Abstractions;
using Guara.Core;
using Guara.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Guara.Engines.Tests;

public class DiagnosticsMiddlewareTests
{
    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JobContext Context()
    {
        var context = new JobContext();
        context.Initialize(new JobId("j1"), new JobDescriptor("Tipo", "Metodo", default, "fila"), attempt: 2);
        return context;
    }

    private static readonly JobDelegate Success = static (_, _) => ValueTask.CompletedTask;
    private static readonly JobDelegate Failure = static (_, _) => throw new InvalidOperationException("quebrou");

    [Fact]
    public async Task Logging_Success_LogsCompletion()
    {
        var logger = new CollectingLogger<LoggingMiddleware>();
        var middleware = new LoggingMiddleware(logger);

        await middleware.InvokeAsync(Context(), Success, Ct);

        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Information && entry.Message.Contains("concluído") && entry.Message.Contains("j1"));
    }

    [Fact]
    public async Task Logging_Failure_LogsWarning_AndRethrows()
    {
        var logger = new CollectingLogger<LoggingMiddleware>();
        var middleware = new LoggingMiddleware(logger);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await middleware.InvokeAsync(Context(), Failure, Ct));

        var failure = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("falhou", failure.Message);
        Assert.IsType<InvalidOperationException>(failure.Exception);
    }

    [Fact]
    public async Task Tracing_EmitsSpanWithTags_AndErrorStatusOnFailure()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == GuaraDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var middleware = new TracingMiddleware();

        await middleware.InvokeAsync(Context(), Success, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await middleware.InvokeAsync(Context(), Failure, Ct));

        Assert.Equal(2, activities.Count);
        Assert.Equal("guara.job", activities[0].OperationName);
        Assert.Equal("j1", activities[0].GetTagItem("job.id"));
        Assert.Equal("fila", activities[0].GetTagItem("job.queue"));
        Assert.Equal(2, activities[0].GetTagItem("job.attempt"));
        Assert.Equal(ActivityStatusCode.Ok, activities[0].Status);
        Assert.Equal(ActivityStatusCode.Error, activities[1].Status);
    }

    [Fact]
    public async Task Metrics_CountProcessedJobs_PerQueueAndOutcome()
    {
        var measurements = new List<(long Value, string? Queue, string? Outcome)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == GuaraDiagnostics.MeterName && instrument.Name == "guara.jobs.processed")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? queue = null;
            string? outcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "queue")
                {
                    queue = tag.Value?.ToString();
                }
                else if (tag.Key == "outcome")
                {
                    outcome = tag.Value?.ToString();
                }
            }

            lock (measurements)
            {
                measurements.Add((value, queue, outcome));
            }
        });
        listener.Start();
        var middleware = new MetricsMiddleware();

        await middleware.InvokeAsync(Context(), Success, Ct);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await middleware.InvokeAsync(Context(), Failure, Ct));

        lock (measurements)
        {
            Assert.Contains(measurements, m => m is { Value: 1, Queue: "fila", Outcome: "success" });
            Assert.Contains(measurements, m => m is { Value: 1, Queue: "fila", Outcome: "failure" });
        }
    }

    [Fact]
    public void UseGuaraDiagnostics_RegistersPipelineMiddlewares_Once()
    {
        var services = new ServiceCollection();
        services.AddGuara().UseGuaraDiagnostics().UseGuaraDiagnostics();
        using var provider = services.BuildServiceProvider();

        var middlewares = provider.GetServices<IJobMiddleware>().ToList();

        Assert.Equal(3, middlewares.Count);
        Assert.IsType<TracingMiddleware>(middlewares[0]);
        Assert.IsType<LoggingMiddleware>(middlewares[1]);
        Assert.IsType<MetricsMiddleware>(middlewares[2]);
    }
}
