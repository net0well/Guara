using System.Text.RegularExpressions;

namespace Guara.Storage.MySql;

/// <summary>
/// Opções do storage MySQL. Configuráveis pela seção <c>Guara:Storage:MySql</c>
/// (a connection string é um segredo: prefira variável de ambiente/secret store —
/// ela nunca é logada pelo Guará).
/// </summary>
public sealed partial class MySqlStorageOptions
{
    /// <summary>Connection string do banco (obrigatória). Exige MySQL 8.0 ou superior.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Prefixo das tabelas do Guará. No MySQL "schema" e "banco de dados" são a mesma
    /// coisa, então o isolamento dentro do banco da aplicação se faz pelo nome da tabela
    /// — não por um schema separado como nos demais providers.
    /// </summary>
    public string TablePrefix { get; set; } = "guara_";

    /// <summary>
    /// Aplica o esquema idempotente no primeiro uso (coordenado por <c>GET_LOCK</c>
    /// entre nós). Em produção, recomenda-se <c>false</c> + migração no pipeline.
    /// </summary>
    public bool AutoMigrate { get; set; } = true;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                "MySqlStorageOptions.ConnectionString é obrigatória. Informe-a em " +
                "UseMySqlStorage(...) ou na seção Guara:Storage:MySql:ConnectionString.");
        }

        // O prefixo é interpolado em DDL/consultas: só identificadores estritos passam.
        // O limite de 40 deixa folga para o maior sufixo dentro dos 64 caracteres do MySQL.
        if (!Prefix().IsMatch(TablePrefix))
        {
            throw new InvalidOperationException(
                $"MySqlStorageOptions.TablePrefix inválido: '{TablePrefix}'. Use apenas letras minúsculas, " +
                "dígitos e '_', começando por letra ou '_' (máx. 40 caracteres).");
        }
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,39}$")]
    private static partial Regex Prefix();
}
