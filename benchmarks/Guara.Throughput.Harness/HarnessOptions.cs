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

    /// <summary>MySQL em container.</summary>
    MySql,
}

/// <summary>O que a rodada mede.</summary>
internal enum HarnessMode
{
    /// <summary>Vazão e latência com worker e dispatcher rodando.</summary>
    Throughput,

    /// <summary>Decomposição do custo de uma aquisição, para achar a causa do teto.</summary>
    Probe,
}

/// <summary>Parâmetros da rodada, lidos da linha de comando.</summary>
internal sealed record HarnessOptions(
    HarnessMode Mode,
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
        var mode = HarnessMode.Throughput;
        var storage = StorageKind.Memory;
        var jobs = 5_000;
        int[] concurrencies = [1, 4, 16, 64];
        var polling = PollingPadrao;

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--mode":
                    mode = Enum.Parse<HarnessMode>(args[i + 1], ignoreCase: true);
                    break;
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

        if (mode == HarnessMode.Probe && storage != StorageKind.PostgreSql)
        {
            throw new ArgumentException("--mode probe exige --storage postgresql.");
        }

        return new HarnessOptions(mode, storage, jobs, concurrencies, polling);
    }

    public static string Ajuda =>
        """
        Uso: dotnet run -c Release -- [opções]

          --mode              throughput | probe                (padrão: throughput)
          --storage           memory | postgresql | sqlserver | mysql   (padrão: memory)
          --jobs              quantidade por rodada             (padrão: 5000)
          --concurrency       lista de MaxConcurrency           (padrão: 1,4,16,64)
          --polling-seconds   teto da espera do dispatcher      (padrão: 60)

        No modo probe, --concurrency vira a lista de profundidades da fila e --jobs
        vira a quantidade de amostras por medição.

        Exemplos:
          dotnet run -c Release -- --storage postgresql --jobs 20000 --concurrency 1,8,32
          dotnet run -c Release -- --mode probe --storage postgresql --jobs 500 --concurrency 100,1000,10000
        """;
}
