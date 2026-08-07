using System.Text.Json.Serialization;
using Guara.Host.Models;

namespace Guara.Host.Endpoints;

/// <summary>
/// Contexto de serialização dos contratos HTTP, gerado em compilação. Sem ele, o
/// System.Text.Json cairia em reflection para ler e escrever estes tipos — que funciona,
/// mas some no trimming e não publica em Native AOT.
/// <para>
/// É o mesmo princípio que o Guará aplica aos argumentos de job: quem conhece os tipos é o
/// compilador, não o runtime.
/// </para>
/// </summary>
[JsonSerializable(typeof(Pedido))]
[JsonSerializable(typeof(IReadOnlyList<Pedido>))]
[JsonSerializable(typeof(CriarPedidoRequest))]
[JsonSerializable(typeof(ExportacaoAceita))]
[JsonSerializable(typeof(string))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class PedidosJsonContext : JsonSerializerContext;
