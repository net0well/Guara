using Microsoft.Data.SqlClient;

namespace Guara.Storage.SqlServer;

/// <summary>
/// Abre conexões para os comandos do provider. O <c>Microsoft.Data.SqlClient</c> não tem
/// um objeto de fonte de dados como o Npgsql: o pooling é do próprio driver e vem da
/// connection string, então abrir por operação é o caminho barato e correto.
/// </summary>
internal sealed class SqlServerConnections(string connectionString)
{
    public async ValueTask<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
