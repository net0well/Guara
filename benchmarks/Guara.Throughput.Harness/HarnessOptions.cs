namespace Guara.Throughput.Harness;

/// <summary>Qual storage sustenta a medição.</summary>
internal enum StorageKind
{
    /// <summary>Sem rede nem disco — o piso, isola o custo do próprio Guará.</summary>
    Memory,

    /// <summary>PostgreSQL em container.</summary>
    PostgreSql,

    /// <summary>SQL Server em container.</summary>
    SqlServer,
}

/// <summary>Parâmetros da rodada, lidos da linha de comando.</summary>
internal sealed record HarnessOptions(
    StorageKind Storage,
    int Jobs,
    int[] Concurrencies,
    TimeSpan PollingInterval)
{
    /// <summary>
    /// Intervalo de busca alto de propósito: com o aviso de fila ligado, o dispatcher
    /// acorda por sinal. Um intervalo curto mascararia um aviso quebrado, e o número
    /// medido passaria a ser o do polling.
    /// </summary>
    private static readonly TimeSpan PollingPadrao = TimeSpan.FromMinutes(1);

    public static HarnessOptions Parse(string[] args)
    {
        var storage = StorageKind.Memory;
        var jobs = 5_000;
        int[] concurrencies = [1, 4, 16, 64];
        var polling = PollingPadrao;

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--storage":
                    storage = Enum.Parse<StorageKind>(args[i + 1], ignoreCase: true);
                    break;
                case "--jobs":
                    jobs = int.Parse(args[i + 1]);
                    break;
                case "--concurrency":
                    concurrencies = [.. args[i + 1].Split(',').Select(int.Parse)];
                    break;
                case "--polling-seconds":
                    polling = TimeSpan.FromSeconds(double.Parse(args[i + 1]));
                    break;
            }
        }

        if (jobs < 1)
        {
            throw new ArgumentException("--jobs precisa ser >= 1.");
        }

        if (concurrencies.Length == 0 || concurrencies.Any(c => c < 1))
        {
            throw new ArgumentException("--concurrency precisa ser uma lista de inteiros >= 1.");
        }

        return new HarnessOptions(storage, jobs, concurrencies, polling);
    }

    public static string Ajuda =>
        """
        Uso: dotnet run -c Release -- [opções]

          --storage           memory | postgresql | sqlserver   (padrão: memory)
          --jobs              quantidade por rodada             (padrão: 5000)
          --concurrency       lista de MaxConcurrency           (padrão: 1,4,16,64)
          --polling-seconds   teto da espera do dispatcher      (padrão: 60)

        Exemplo:
          dotnet run -c Release -- --storage postgresql --jobs 20000 --concurrency 1,8,32
        """;
}
