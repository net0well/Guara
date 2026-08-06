using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Guara.Abstractions;
using Guara.Dashboard.Api;
using Guara.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Guara.Dashboard.Api.Tests;

public sealed class DashboardApiTests : IAsyncDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private WebApplication? _app;

    private async Task<(HttpClient Client, IStorage Storage, IServiceProvider Services)> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddGuara(options => options.ApplicationName = "dashboard-teste")
            .UseMemoryStorage()
            .AddGuaraScheduler()
            .AddGuaraDashboardApi();

        _app = builder.Build();
        _app.MapGuaraDashboardApi();
        await _app.StartAsync(Ct);

        return (_app.GetTestClient(), _app.Services.GetRequiredService<IStorage>(), _app.Services);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private static JobRecord NewJob(
        string id, JobState state = JobState.Enqueued, string queue = "default",
        DateTimeOffset? scheduledFor = null) => new()
    {
        Id = new JobId(id),
        Descriptor = new JobDescriptor("Demo.Tipo", "Executar", default, queue),
        State = state,
        Queue = queue,
        CreatedAt = T0,
        ScheduledFor = scheduledFor,
    };

    [Fact]
    public async Task Stats_CountsJobsByState()
    {
        var (client, storage, _) = await StartAsync();
        await storage.Jobs.CreateAsync(NewJob("e1"), Ct);
        await storage.Jobs.CreateAsync(NewJob("e2"), Ct);
        await storage.Jobs.CreateAsync(NewJob("f1"), Ct);
        await storage.Jobs.UpdateStateAsync(new JobId("f1"), JobState.Failed, "erro", Ct);

        var stats = await client.GetFromJsonAsync<StatsDto>("/api/v1/stats", Json, Ct);

        Assert.NotNull(stats);
        Assert.Equal(3, stats.Total);
        Assert.Equal(2, stats.ByState["Enqueued"]);
        Assert.Equal(1, stats.ByState["Failed"]);
    }

    [Fact]
    public async Task Jobs_ListsPaginated_WithStateFilter_AndCapsPageSize()
    {
        var (client, storage, _) = await StartAsync();
        for (var i = 0; i < 3; i++)
        {
            await storage.Jobs.CreateAsync(NewJob($"j{i}"), Ct);
        }

        await storage.Jobs.UpdateStateAsync(new JobId("j0"), JobState.Failed, "x", Ct);

        var failed = await client.GetFromJsonAsync<PageDto<JobSummaryDto>>(
            "/api/v1/jobs?state=failed", Json, Ct);
        Assert.NotNull(failed);
        var job = Assert.Single(failed.Items);
        Assert.Equal("j0", job.Id);
        Assert.Equal("Failed", job.State);

        var capped = await client.GetFromJsonAsync<PageDto<JobSummaryDto>>(
            "/api/v1/jobs?pageSize=9999", Json, Ct);
        Assert.Equal(JobQuery.MaxPageSize, capped!.PageSize); // teto aplicado

        var invalid = await client.GetAsync("/api/v1/jobs?state=inexistente", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task JobDetail_ReturnsData_Or404()
    {
        var (client, storage, _) = await StartAsync();
        await storage.Jobs.CreateAsync(NewJob("j1", queue: "relatorios"), Ct);

        var detail = await client.GetFromJsonAsync<JobDetailDto>("/api/v1/jobs/j1", Json, Ct);
        Assert.NotNull(detail);
        Assert.Equal("Demo.Tipo", detail.TypeName);
        Assert.Equal("relatorios", detail.Queue);

        var missing = await client.GetAsync("/api/v1/jobs/fantasma", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Retry_RequeuesFailedJob_AndRejectsOthers()
    {
        var (client, storage, _) = await StartAsync();
        await storage.Jobs.CreateAsync(NewJob("falho"), Ct);
        await storage.Jobs.UpdateStateAsync(new JobId("falho"), JobState.Failed, "erro", Ct);
        await storage.Jobs.CreateAsync(NewJob("ok"), Ct);

        var retry = await client.PostAsync("/api/v1/jobs/falho/retry", null, Ct);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        var job = await storage.Jobs.GetAsync(new JobId("falho"), Ct);
        Assert.Equal(JobState.Scheduled, job!.State); // elegível de novo

        var conflict = await client.PostAsync("/api/v1/jobs/ok/retry", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var missing = await client.PostAsync("/api/v1/jobs/fantasma/retry", null, Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Trigger_MakesScheduledJobEligibleNow()
    {
        var (client, storage, _) = await StartAsync();
        await storage.Jobs.CreateAsync(
            NewJob("agendado", JobState.Scheduled, scheduledFor: T0.AddYears(1)), Ct);

        var response = await client.PostAsync("/api/v1/jobs/agendado/trigger", null, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var job = await storage.Jobs.GetAsync(new JobId("agendado"), Ct);
        Assert.True(job!.ScheduledFor <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Delete_RemovesJob_WithHonest404And409()
    {
        var (client, storage, _) = await StartAsync();
        await storage.Jobs.CreateAsync(NewJob("j1"), Ct);
        await storage.Jobs.CreateAsync(NewJob("rodando"), Ct);
        await storage.Jobs.AcquireNextDueAsync("default", 1, TimeSpan.FromMinutes(5), T0, Ct); // j1 (mais elegível)

        var running = await client.DeleteAsync("/api/v1/jobs/j1", Ct);
        Assert.Equal(HttpStatusCode.Conflict, running.StatusCode); // Processing

        var ok = await client.DeleteAsync("/api/v1/jobs/rodando", Ct);
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        var missing = await client.DeleteAsync("/api/v1/jobs/fantasma", Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task QueuesServersAndRecurring_AreListed()
    {
        var (client, storage, _) = await StartAsync();
        await storage.Jobs.CreateAsync(NewJob("j1", queue: "alta"), Ct);
        await storage.Servers.AnnounceAsync(new ServerNode
        {
            Id = "n1", MachineName = "maquina", StartedAt = T0, LastHeartbeat = T0,
            Queues = ["alta"], MaxConcurrency = 4,
        }, Ct);
        await storage.Recurring.UpsertAsync(new RecurringJobRecord
        {
            Id = "diario",
            Descriptor = new JobDescriptor("Demo.Tipo", "Executar", default),
            CronExpression = "0 3 * * *",
            CreatedAt = T0,
            NextRunAt = T0.AddHours(1),
        }, Ct);

        var queues = await client.GetFromJsonAsync<List<QueueDto>>("/api/v1/queues", Json, Ct);
        Assert.Contains(queues!, q => q is { Name: "alta", Length: 1 });

        var servers = await client.GetFromJsonAsync<List<ServerDto>>("/api/v1/servers", Json, Ct);
        var server = Assert.Single(servers!);
        Assert.Equal("n1", server.Id);
        Assert.Equal(4, server.MaxConcurrency);

        var recurring = await client.GetFromJsonAsync<List<RecurringDto>>("/api/v1/recurring", Json, Ct);
        var definition = Assert.Single(recurring!);
        Assert.Equal("diario", definition.Id);
        Assert.Equal("0 3 * * *", definition.CronExpression);
    }

    [Fact]
    public async Task Stream_DeliversJobEvents_AsSse()
    {
        var (client, _, services) = await StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/stream");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);

        await using var body = await response.Content.ReadAsStreamAsync(Ct);
        using var reader = new StreamReader(body);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        // Publicar uma vez só é uma corrida: os cabeçalhos chegam antes de o handler
        // registrar o canal do assinante, e um evento emitido nessa janela se perde para
        // sempre — o stream entrega o que acontece depois da inscrição, não o histórico.
        // Republicar até o leitor ver o evento remove a corrida sem afrouxar a asserção.
        var events = services.GetRequiredService<IEventPublisher>();
        using var publishing = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var publisher = Task.Run(
            async () =>
            {
                while (!publishing.IsCancellationRequested)
                {
                    await events.PublishAsync(new JobCompleted(new JobId("j-sse"), T0), publishing.Token);
                    await Task.Delay(TimeSpan.FromMilliseconds(100), publishing.Token);
                }
            },
            publishing.Token);

        var lines = new List<string>();
        while (!timeout.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(timeout.Token);
            if (line is null)
            {
                break;
            }

            lines.Add(line);
            if (line.StartsWith("data:", StringComparison.Ordinal) && line.Contains("j-sse"))
            {
                break;
            }
        }

        await publishing.CancelAsync();
        try
        {
            await publisher;
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Contains(lines, l => l.StartsWith("event: job", StringComparison.Ordinal));
        var data = Assert.Single(lines, l => l.StartsWith("data:", StringComparison.Ordinal) && l.Contains("j-sse"));
        Assert.Contains("\"kind\":\"completed\"", data);
    }
}
