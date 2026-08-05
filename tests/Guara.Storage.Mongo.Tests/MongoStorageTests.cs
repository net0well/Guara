using Guara.Abstractions;
using Guara.Storage.Conformance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace Guara.Storage.Mongo.Tests;

[Collection("mongo")]
public sealed class MongoStorageTests(MongoContainerFixture fixture)
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
    public async Task IndexCreation_IsIdempotent_UnderConcurrentBoot()
    {
        var options = fixture.NewOptions();
        var first = new MongoStorage(options, new ManualTimeProvider(T0));
        var second = new MongoStorage(new MongoStorageOptions
        {
            ConnectionString = options.ConnectionString,
            Database = options.Database,
            CollectionPrefix = options.CollectionPrefix,
        }, new ManualTimeProvider(T0));

        // Dois nós sobem juntos: a criação de índice converge sem lock de migração.
        await Task.WhenAll(
            first.Jobs.CreateAsync(NewJob("a"), Ct).AsTask(),
            second.Jobs.CreateAsync(NewJob("b"), Ct).AsTask());

        Assert.NotNull(await first.Jobs.GetAsync(new JobId("b"), Ct));
        Assert.NotNull(await second.Jobs.GetAsync(new JobId("a"), Ct));
    }

    [Fact]
    public async Task CustomPrefix_HoldsAllGuaraCollections()
    {
        var options = fixture.NewOptions();
        var storage = new MongoStorage(options, new ManualTimeProvider(T0));
        // Coleção no MongoDB nasce na primeira escrita: só as tocadas por este teste existem.
        await storage.Jobs.CreateAsync(NewJob("a"), Ct);
        await storage.Recurring.UpsertCalendarAsync(new CalendarRecord { Name = "feriados" }, Ct);

        var database = new MongoClient(options.ConnectionString).GetDatabase(options.Database);
        var nomes = await (await database.ListCollectionNamesAsync(cancellationToken: Ct)).ToListAsync(Ct);

        Assert.Contains($"{options.CollectionPrefix}jobs", nomes);
        Assert.Contains($"{options.CollectionPrefix}calendars", nomes);
        // O prefixo é o que isola o Guará: nada dele escapa para fora dele.
        Assert.All(
            nomes.Where(nome => nome.StartsWith(options.CollectionPrefix, StringComparison.Ordinal)),
            nome => Assert.Contains(nome[options.CollectionPrefix.Length..],
                new[] { "jobs", "servers", "locks", "recurring", "calendars", "continuations" }));
    }

    [Theory]
    [InlineData("Guara_")]
    [InlineData("guara.jobs")]
    [InlineData("guará_")]
    public void InvalidPrefix_FailsFast(string prefix)
    {
        var options = fixture.NewOptions();
        options.CollectionPrefix = prefix;

        var ex = Assert.Throws<InvalidOperationException>(() => new MongoStorage(options));
        Assert.Contains("CollectionPrefix", ex.Message);
    }

    [Fact]
    public void MissingDatabase_FailsFast()
    {
        // Connection string sem banco e sem Database configurado: não há onde gravar.
        var ex = Assert.Throws<InvalidOperationException>(() => new MongoStorage(new MongoStorageOptions
        {
            ConnectionString = "mongodb://localhost:27017",
        }));

        Assert.Contains("banco", ex.Message);
    }

    [Fact]
    public async Task Descriptor_IsQueryableDocument_NotOpaqueBlob()
    {
        var options = fixture.NewOptions();
        var storage = new MongoStorage(options, new ManualTimeProvider(T0));
        var descriptor = new JobDescriptor(
            "Meu.Tipo", "MeuMetodo", new byte[] { 1, 2, 3, 255 }, "relatorios")
        {
            Metadata = new Dictionary<string, string> { ["correlacao"] = "abc-123", ["chave.com.ponto"] = "acme" },
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
        // Chave com ponto é nome proibido de campo no MongoDB: a lista de pares aceita.
        Assert.Equal("acme", found.Descriptor.Metadata["chave.com.ponto"]);

        // O descritor é documento de verdade, então o servidor consulta por dentro dele.
        var collection = new MongoClient(options.ConnectionString)
            .GetDatabase(options.Database)
            .GetCollection<BsonDocument>($"{options.CollectionPrefix}jobs");
        Assert.Equal(1, await collection.CountDocumentsAsync(
            new BsonDocument("descriptor.typeName", "Meu.Tipo"), cancellationToken: Ct));
    }

    [Fact]
    public async Task Recurring_FullDefinition_RoundTrips()
    {
        var storage = new MongoStorage(fixture.NewOptions(), new ManualTimeProvider(T0));
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
    public async Task Instants_KeepSubMillisecondPrecision()
    {
        var storage = new MongoStorage(fixture.NewOptions(), new ManualTimeProvider(T0));
        // Data BSON tem precisão de milissegundo; ticks preservam os 100 ns do original.
        var comTicksSoltos = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);
        await storage.Jobs.CreateAsync(NewJob("j1") with { CreatedAt = comTicksSoltos }, Ct);

        var found = await storage.Jobs.GetAsync(new JobId("j1"), Ct);

        Assert.NotNull(found);
        Assert.Equal(comTicksSoltos, found.CreatedAt);
        Assert.Equal(comTicksSoltos.UtcTicks, found.CreatedAt.UtcTicks);
    }

    [Fact]
    public async Task ServerQueues_RoundTrip_WithSeparatorsInNames()
    {
        var storage = new MongoStorage(fixture.NewOptions(), new ManualTimeProvider(T0));
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
        var storage = new MongoStorage(fixture.NewOptions(), new ManualTimeProvider(T0));
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
    public async Task UseMongoStorage_BindsFromConfigurationSection()
    {
        var options = fixture.NewOptions();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Guara:Storage:Mongo:ConnectionString"] = options.ConnectionString,
            ["Guara:Storage:Mongo:Database"] = options.Database,
            ["Guara:Storage:Mongo:CollectionPrefix"] = options.CollectionPrefix,
        }).Build();

        var services = new ServiceCollection();
        services.AddGuara()
            .UseConfiguration(configuration)
            .UseMongoStorage();
        await using var provider = services.BuildServiceProvider();

        var storage = provider.GetRequiredService<IStorage>();
        await storage.Jobs.CreateAsync(NewJob("via-config"), Ct);
        Assert.NotNull(await storage.Jobs.GetAsync(new JobId("via-config"), Ct));
    }
}
