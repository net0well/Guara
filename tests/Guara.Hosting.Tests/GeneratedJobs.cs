using Guara.Abstractions;

namespace Guara.Hosting.Tests;

/// <summary>Serviço injetado nos jobs gerados (prova a resolução por DI).</summary>
public sealed class SaudacaoServico
{
    private readonly List<string> _mensagens = [];

    public IReadOnlyList<string> Mensagens
    {
        get
        {
            lock (_mensagens)
            {
                return [.. _mensagens];
            }
        }
    }

    public void Registrar(string mensagem)
    {
        lock (_mensagens)
        {
            _mensagens.Add(mensagem);
        }
    }
}

/// <summary>Jobs descobertos pelo source generator nos testes de ponta a ponta.</summary>
public sealed class SaudacaoJobs(SaudacaoServico servico)
{
    [GuaraJob]
    [GuaraFila("saudacoes")]
    public Task SaudarAsync(string nome, int vezes, CancellationToken ct)
    {
        for (var i = 0; i < vezes; i++)
        {
            servico.Registrar($"olá, {nome}");
        }

        return Task.CompletedTask;
    }

    [GuaraJob]
    [GuaraRetentativas(0)]
    public Task FalharAsync() => throw new InvalidOperationException("sempre falha");
}
