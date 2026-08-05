using System.Text.RegularExpressions;

namespace Guara.Storage.SqlServer;

/// <summary>
/// Opções do storage SQL Server. Configuráveis pela seção
/// <c>Guara:Storage:SqlServer</c> (a connection string é um segredo: prefira
/// variável de ambiente/secret store — ela nunca é logada pelo Guará).
/// </summary>
public sealed partial class SqlServerStorageOptions
{
    /// <summary>Connection string do banco (obrigatória).</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Schema em que as tabelas do Guará vivem — isola o Guará do resto do banco,
    /// permitindo usar o mesmo banco da aplicação sem conflito.
    /// </summary>
    public string Schema { get; set; } = "guara";

    /// <summary>
    /// Aplica o esquema idempotente no primeiro uso (coordenado por <c>sp_getapplock</c>
    /// entre nós). Em produção, recomenda-se <c>false</c> + migração no pipeline.
    /// </summary>
    public bool AutoMigrate { get; set; } = true;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                "SqlServerStorageOptions.ConnectionString é obrigatória. Informe-a em " +
                "UseSqlServerStorage(...) ou na seção Guara:Storage:SqlServer:ConnectionString.");
        }

        // O schema é interpolado em DDL/consultas: só identificadores estritos passam.
        if (!SchemaName().IsMatch(Schema))
        {
            throw new InvalidOperationException(
                $"SqlServerStorageOptions.Schema inválido: '{Schema}'. Use apenas letras minúsculas, " +
                "dígitos e '_', começando por letra ou '_' (máx. 63 caracteres).");
        }
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex SchemaName();
}
