namespace Guara.Abstractions;

/// <summary>
/// Marca um método como job do Guará: o source generator o descobre em compilação e
/// gera a invocação sem reflection, o registro em DI e a factory tipada de descritor.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class GuaraJobAttribute : Attribute;

/// <summary>
/// Marcador de assembly que habilita a geração do registro de jobs
/// (<c>[assembly: GuaraJobs]</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class GuaraJobsAttribute : Attribute;

/// <summary>Define a fila em que o job entra (default: <c>"default"</c>).</summary>
/// <param name="nome">Nome da fila.</param>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class GuaraFilaAttribute(string nome) : Attribute
{
    /// <summary>Nome da fila.</summary>
    public string Nome { get; } = nome;
}

/// <summary>
/// Política de retentativa do job — sobrepõe o default global. <c>0</c> = nunca
/// retentar (use em jobs com efeito colateral irreversível).
/// </summary>
/// <param name="maximo">Máximo de retentativas após a primeira falha.</param>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class GuaraRetentativasAttribute(int maximo) : Attribute
{
    /// <summary>Máximo de retentativas após a primeira falha (0 = nunca).</summary>
    public int Maximo { get; } = maximo;
}

/// <summary>
/// Exclusão mútua: no máximo uma execução simultânea por chave, mesmo entre nós
/// (via lock do storage). Execução com a chave ocupada volta para a fila — o worker
/// nunca fica bloqueado e o job nunca executa em dobro.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class GuaraDesabilitarConcorrenciaAttribute : Attribute
{
    /// <summary>
    /// Chave do mutex; default <c>"{tipo}.{metodo}"</c>. Aceita placeholders de
    /// argumento (<c>"cliente-{0}"</c>) — validados em compilação.
    /// </summary>
    public string? Chave { get; init; }

    /// <summary>
    /// Quanto tempo aguardar pela chave antes de devolver o job à fila
    /// (<c>0</c> = devolve imediatamente).
    /// </summary>
    public int EsperaSegundos { get; init; }
}

/// <summary>
/// Tempo máximo de execução; ao exceder, o token do job é cancelado
/// (cooperativo — o job deve honrá-lo).
/// </summary>
/// <param name="segundos">Limite em segundos.</param>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class GuaraTempoLimiteAttribute(int segundos) : Attribute
{
    /// <summary>Limite em segundos.</summary>
    public int Segundos { get; } = segundos;
}

/// <summary>
/// Para jobs recorrentes: pula a nova ocorrência (registrando o pulo) se a anterior
/// ainda está em execução. Sem o atributo, ocorrências sobrepõem por padrão.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class GuaraPularSeAnteriorEmExecucaoAttribute : Attribute;
