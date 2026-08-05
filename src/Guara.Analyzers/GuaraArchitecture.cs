using System;
using System.Collections.Immutable;

namespace Guara.Analyzers;

/// <summary>
/// O grafo de dependências do Guará traduzido em camadas comparáveis. As setas apontam
/// sempre para baixo: um pacote pode referenciar quem está numa camada menor ou igual, e
/// nunca quem está acima. <c>Guara.Abstractions</c> é a folha e não referencia ninguém.
/// </summary>
internal static class GuaraArchitecture
{
    /// <summary>Prefixo que identifica um assembly do produto.</summary>
    public const string Prefixo = "Guara.";

    /// <summary>Pacote de contratos de storage; os providers implementam, ele não implementa.</summary>
    public const string ContratosDeStorage = "Guara.Storage";

    /// <summary>
    /// Camada de cada pacote. Ausente significa desconhecido — pacote de terceiro ou
    /// componente ainda não catalogado, que a regra ignora em vez de acusar sem base.
    /// </summary>
    private static readonly ImmutableDictionary<string, int> Camadas =
        ImmutableDictionary.CreateRange(StringComparer.Ordinal, new[]
        {
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Abstractions", 0),
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Core", 1),
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Storage", 1),
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Configuration", 1),
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Serialization", 2),
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Diagnostics", 2),
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Redis", 2),
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Scheduler", 3),
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Dispatcher", 3),
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Worker", 3),
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Executor", 3),
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Hosting", 4),
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Server", 4),
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Authorization", 4),
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Authentication", 4),
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Dashboard.Api", 5),
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Dashboard", 6),
            new System.Collections.Generic.KeyValuePair<string, int>("Guara.Cli", 6),
        });

    /// <summary>Camada de providers de storage: acima dos contratos, abaixo dos motores.</summary>
    private const int CamadaDeProvider = 2;

    /// <summary>Motores nunca podem alcançar um provider concreto; falam com o contrato.</summary>
    private static readonly ImmutableHashSet<string> Motores =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "Guara.Core", "Guara.Scheduler", "Guara.Dispatcher", "Guara.Worker", "Guara.Executor", "Guara.Server");

    /// <summary>Descobre a camada de um assembly do produto.</summary>
    /// <param name="assembly">Nome simples do assembly.</param>
    /// <param name="camada">Camada encontrada.</param>
    /// <returns><c>true</c> quando o assembly é catalogado.</returns>
    public static bool TentarObterCamada(string assembly, out int camada)
    {
        if (Camadas.TryGetValue(assembly, out camada))
        {
            return true;
        }

        if (EhProviderDeStorage(assembly))
        {
            camada = CamadaDeProvider;
            return true;
        }

        camada = 0;
        return false;
    }

    /// <summary>
    /// Um provider é <c>Guara.Storage.{Tecnologia}</c>. O próprio <c>Guara.Storage</c> não
    /// é provider: ele define os contratos que os providers implementam.
    /// </summary>
    /// <param name="assembly">Nome simples do assembly.</param>
    /// <returns><c>true</c> quando o assembly é um provider de storage.</returns>
    public static bool EhProviderDeStorage(string assembly)
        => assembly.StartsWith(ContratosDeStorage + ".", StringComparison.Ordinal);

    /// <summary>
    /// Pacotes que ligam um contrato do Guará a uma tecnologia concreta e não seguem o
    /// nome <c>Guara.Storage.{Tecnologia}</c>. Alcançá-los prende o motor à tecnologia
    /// exatamente como alcançar um provider de storage.
    /// </summary>
    private static readonly ImmutableHashSet<string> OutrasImplementacoesConcretas =
        ImmutableHashSet.Create(StringComparer.Ordinal, "Guara.Redis");

    /// <summary>
    /// Indica se o assembly implementa um contrato do Guará sobre uma tecnologia
    /// específica — o que os motores nunca podem alcançar.
    /// </summary>
    /// <param name="assembly">Nome simples do assembly.</param>
    /// <returns><c>true</c> quando o assembly é uma implementação concreta.</returns>
    public static bool EhImplementacaoConcreta(string assembly)
        => EhProviderDeStorage(assembly) || OutrasImplementacoesConcretas.Contains(assembly);

    /// <summary>Indica se o assembly é um motor de execução do Guará.</summary>
    /// <param name="assembly">Nome simples do assembly.</param>
    /// <returns><c>true</c> quando o assembly é um motor.</returns>
    public static bool EhMotor(string assembly) => Motores.Contains(assembly);

    /// <summary>Indica se o assembly pertence ao produto.</summary>
    /// <param name="assembly">Nome simples do assembly.</param>
    /// <returns><c>true</c> quando o nome começa com o prefixo do Guará.</returns>
    public static bool EhDoGuara(string assembly)
        => assembly.StartsWith(Prefixo, StringComparison.Ordinal);
}
