using Guara.Host.Models;
using Guara.Host.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Guara.Host.Endpoints;

/// <summary>Corpo da requisição de criação de pedido.</summary>
/// <param name="EmailCliente">E-mail de quem está comprando.</param>
/// <param name="Total">Valor total da compra.</param>
public sealed record CriarPedidoRequest(string EmailCliente, decimal Total);

/// <summary>Resposta do pedido de exportação.</summary>
/// <param name="JobId">Id do job, acompanhável no painel.</param>
public sealed record ExportacaoAceita(string JobId);

/// <summary>
/// Entrada HTTP do fluxo de pedidos, em Minimal APIs.
/// <para>
/// Minimal API, e não controllers MVC, por um motivo concreto: o MVC não suporta trimming
/// nem Native AOT, e o exemplo do Guará não pode abrir exceção justamente na promessa que
/// o framework faz. O mesmo vale para a serialização — as respostas passam por um
/// <c>JsonSerializerContext</c> gerado em compilação, sem reflection.
/// </para>
/// <para>
/// Os endpoints não conhecem o Guará: chamam o serviço, que decide o que vira job. É essa
/// fronteira que mantém o enfileiramento testável e fora da camada web.
/// </para>
/// </summary>
public static class PedidosEndpoints
{
    /// <summary>Registra as rotas de pedidos.</summary>
    /// <param name="app">Construtor de rotas da aplicação.</param>
    /// <returns>O grupo criado, para encadear configuração.</returns>
    public static RouteGroupBuilder MapPedidos(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/pedidos").WithTags("Pedidos");

        grupo.MapPost("/", CriarAsync)
            .WithName("CriarPedido")
            .WithSummary("Cria um pedido e dispara confirmação e cobrança em segundo plano.");

        grupo.MapGet("/{id:int}", ObterAsync)
            .WithName("ObterPedido")
            .WithSummary("Busca um pedido pelo id.");

        grupo.MapGet("/", ListarAsync)
            .WithName("ListarPedidos")
            .WithSummary("Lista os pedidos, do mais recente para o mais antigo.");

        grupo.MapPost("/exportacoes", ExportarAsync)
            .WithName("ExportarPedidos")
            .WithSummary("Pede a exportação dos pedidos de um cliente.");

        return grupo;
    }

    private static async Task<Results<Created<Pedido>, BadRequest<string>>> CriarAsync(
        CriarPedidoRequest request,
        PedidoService pedidos,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.EmailCliente) || request.Total <= 0)
        {
            return TypedResults.BadRequest("Informe o e-mail do cliente e um total maior que zero.");
        }

        // Retorna assim que o pedido está gravado. A confirmação e a cobrança já estão
        // enfileiradas, e o cliente não espera por nenhuma das duas.
        var pedido = await pedidos.RegistrarAsync(request.EmailCliente, request.Total, ct);
        return TypedResults.Created($"/api/pedidos/{pedido.Id}", pedido);
    }

    private static async Task<Results<Ok<Pedido>, NotFound>> ObterAsync(
        int id,
        PedidoService pedidos,
        CancellationToken ct)
    {
        var pedido = await pedidos.ObterAsync(id, ct);
        return pedido is null ? TypedResults.NotFound() : TypedResults.Ok(pedido);
    }

    private static async Task<Ok<IReadOnlyList<Pedido>>> ListarAsync(
        PedidoService pedidos,
        CancellationToken ct)
        => TypedResults.Ok(await pedidos.ListarAsync(ct));

    private static async Task<Accepted<ExportacaoAceita>> ExportarAsync(
        string email,
        PedidoService pedidos,
        CancellationToken ct)
    {
        var jobId = await pedidos.SolicitarExportacaoAsync(email, ct);

        // 202: o trabalho foi aceito, não concluído. O id devolvido é o mesmo que aparece
        // no painel, então quem chamou consegue acompanhar.
        return TypedResults.Accepted(
            $"/guara/jobs/{jobId.Value}", new ExportacaoAceita(jobId.Value));
    }
}
