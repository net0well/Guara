using Guara.Abstractions;
using Guara.Storage.Conformance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Xunit;

namespace Guara.Storage.MySql.Tests;

[Collection("mysql")]
public sealed class MySqlStorageTests(MySqlContainerFixture fixture)
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JobRecord NewJob(string id) => new()
    {
        Id = new JobId(id),
        Descriptor = new JobDescriptor("Tipo", "Metodo", default),
        State = JobState.Enqueued,
        CreatedAt = T0,
    };

    [Fact]
    public async Task Migration_IsIdempotent_UnderConcurrentBoot()
    {
        var options = fixture.NewOptions();
        await using var first = new MySqlStorage(options, new ManualTimeProvider(T0));
        await using var second = new MySqlStorage(new MySqlStorageOptions
        {
            ConnectionString = options.ConnectionString,
            TablePrefix = options.TablePrefix,
        }, new ManualTimeProvider(T0));

        // Dois nós sobem juntos: a migração idempotente sob GET_LOCK serializa.
        await Task.WhenAll(
            first.Jobs.CreateAsync(NewJob("a"), Ct).AsTask(),
            second.Jobs.CreateAsync(NewJob("b"), Ct).AsTask());

        Assert.NotNull(await first.Jobs.GetAsync(new JobId("b"), Ct));
        Assert.NotNull(await second.Jobs.GetAsync(new JobId("a"), Ct));
    }

    [Fact]
    public async Task CustomPrefix_HoldsAllGuaraTables()
    {
        var options = fixture.NewOptions();
        await using var storage = new MySqlStorage(options, new ManualTimeProvider(T0));
        await storage.Jobs.CreateAsync(NewJob("a"), Ct);

        await using var connection = new MySqlConnection(options.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        // O prefixo é o que isola o Guará no banco da aplicação: só as tabelas dele batem.
        command.CommandText = """
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name LIKE CONCAT(@prefixo, '%')
            """;
        command.Parameters.AddWithValue("@prefixo", options.TablePrefix);

        Assert.Equal(7L, await command.ExecuteScalarAsync(Ct));
    }

    [Theory]
    [InlineData("Guara_")]
    [InlineData("guara;drop table x")]
    [InlineData("guará_")]
    public void InvalidPrefix_FailsFast(string prefix)
    {
        var options = fixture.NewOptions();
        options.TablePrefix = prefix;

        var ex = Assert.Throws<InvalidOperationException>(() => new MySqlStorage(options));
        Assert.Contains("TablePrefix", ex.Message);
    }

    [Fact]
    public async Task Descriptor_RoundTrips_ArgumentsMetadataAndQueue()
    {
        await using var storage = new MySqlStorage(fixture.NewOptions(), new ManualTimeProvider(T0));
        var descriptor = new JobDescriptor(
            "Meu.Tipo", "MeuMetodo", new byte[] { 1, 2, 3, 255 }, "relatorios")
        {
            Metadata = new Dictionary<string, string> { ["correlacao"] = "abc-123", ["tenant"] = "acme" },
        };
        await storage.Jobs.CreateAsync(NewJob("j1") with { Descriptor = descriptor, Queue = "relatorios" }, Ct);

        var found = await storage.Jobs.GetAsync(new JobId("j1"), Ct);

        Assert.NotNull(found);
        Assert.Equal("Meu.Tipo", found.Descriptor.TypeName);
        Assert.Equal("MeuMetodo", found.Descriptor.MethodName);
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, found.Descriptor.Arguments.ToArray());
        Assert.Equal("relatorios", found.Descriptor.Queue);
        Assert.NotNull(found.Descriptor.Metadata);
        Assert.Equal("abc-123", found.Descriptor.Metadata["correlacao"]);
        Assert.Equal("acme", found.Descriptor.Metadata["tenant"]);
    }

    [Fact]
    public async Task Recurring_FullDefinition_RoundTrips()
    {
        await using var storage = new MySqlStorage(fixture.NewOptions(), new ManualTimeProvider(T0));
        var record = new RecurringJobRecord
        {
            Id = "completo",
            Descriptor = new JobDescriptor("Tipo", "Metodo", default),
            Interval = TimeSpan.FromMinutes(10),
            WindowStart = new TimeOnly(22, 0),
            WindowEnd = new TimeOnly(6, 0),
            TimeZoneId = "America/Sao_Paulo",
            NotBefore = T0,
            NotAfter = T0.AddYears(1),
            Description = "Janela noturna",
            Queue = "manutencao",
            CalendarName = "feriados",
            SkipIfPreviousRunning = true,
            Paused = true,
            CreatedAt = T0,
            LastRunAt = T0.AddMinutes(-10),
            LastRunJobId = new JobId("ultimo"),
            NextRunAt = T0.AddMinutes(10),
            LastSkippedAt = T0.AddMinutes(-5),
        };

        await storage.Recurring.UpsertAsync(record, Ct);
        var found = await storage.Recurring.GetAsync("completo", Ct);

        Assert.NotNull(found);
        Assert.Equal("Tipo", found.Descriptor.TypeName);
        // ReadOnlyMemory não tem igualdade por conteúdo: compara o restante do registro
        // com o descriptor original no lugar do desserializado.
        Assert.Equal(record, found with { Descriptor = record.Descriptor });
    }

    [Fact]
    public async Task Instants_RoundTrip_InUtc_RegardlessOfOffset()
    {
        await using var storage = new MySqlStorage(fixture.NewOptions(), new ManualTimeProvider(T0));
        // O MySQL não tem tipo com fuso: gravado em -03:00, tem que voltar como o mesmo
        // instante em UTC, não como o mesmo relógio de parede.
        var comFuso = new DateTimeOffset(2026, 7, 18, 9, 0, 0, TimeSpan.FromHours(-3));
        await storage.Jobs.CreateAsync(NewJob("j1") with { CreatedAt = comFuso }, Ct);

        var found = await storage.Jobs.GetAsync(new JobId("j1"), Ct);

        Assert.NotNull(found);
        Assert.Equal(comFuso, found.CreatedAt);
        Assert.Equal(TimeSpan.Zero, found.CreatedAt.Offset);
    }

    [Fact]
    public async Task ServerQueues_RoundTrip_WithSeparatorsInNames()
    {
        await using var storage = new MySqlStorage(fixture.NewOptions(), new ManualTimeProvider(T0));
        string[] queues = ["default", "fila, com virgula", "fila|com|pipe"];
        await storage.Servers.AnnounceAsync(new ServerNode
        {
            Id = "no-1",
            MachineName = "maquina",
            StartedAt = T0,
            LastHeartbeat = T0,
            Queues = queues,
            MaxConcurrency = 4,
        }, Ct);

        var found = Assert.Single(await storage.Servers.ListAsync(Ct));
        Assert.Equal(queues, found.Queues);
    }

    [Fact]
    public async Task Continuation_ResolveRace_HasExactlyOneWinner()
    {
        await using var storage = new MySqlStorage(fixture.NewOptions(), new ManualTimeProvider(T0));
        await storage.Continuations.AddAsync(new ContinuationRecord
        {
            ChildId = new JobId("filho"),
            ParentId = new JobId("pai"),
            CreatedAt = T0,
        }, Ct);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
            await storage.Continuations.TryResolveAsync(
                new JobId("filho"), ContinuationStatus.Enqueued, null, T0, Ct))));

        Assert.Equal(1, attempts.Count(won => won));
    }

    [Fact]
    public async Task UseMySqlStorage_BindsFromConfigurationSection()
    {
        var options = fixture.NewOptions();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Guara:Storage:MySql:ConnectionString"] = options.ConnectionString,
            ["Guara:Storage:MySql:TablePrefix"] = options.TablePrefix,
        }).Build();

        var services = new ServiceCollection();
        services.AddGuara()
            .UseConfiguration(configuration)
            .UseMySqlStorage();
        await using var provider = services.BuildServiceProvider();

        var storage = provider.GetRequiredService<IStorage>();
        await storage.Jobs.CreateAsync(NewJob("via-config"), Ct);
        Assert.NotNull(await storage.Jobs.GetAsync(new JobId("via-config"), Ct));
    }
}
