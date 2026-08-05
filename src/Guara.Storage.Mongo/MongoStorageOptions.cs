using System.Text.RegularExpressions;

namespace Guara.Storage.Mongo;

/// <summary>
/// Opções do storage MongoDB. Configuráveis pela seção <c>Guara:Storage:Mongo</c>
/// (a connection string é um segredo: prefira variável de ambiente/secret store —
/// ela nunca é logada pelo Guará).
/// </summary>
public sealed partial class MongoStorageOptions
{
    /// <summary>Connection string do cluster (obrigatória).</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Banco em que as coleções do Guará vivem. Vazio usa o banco declarado na própria
    /// connection string; se ela também não trouxer um, a validação falha.
    /// </summary>
    public string Database { get; set; } = "";

    /// <summary>
    /// Prefixo das coleções do Guará — isola o Guará do resto do banco, permitindo usar
    /// o mesmo banco da aplicação sem conflito de nomes.
    /// </summary>
    public string CollectionPrefix { get; set; } = "guara_";

    /// <summary>
    /// Cria os índices no primeiro uso. A criação de índice no MongoDB é idempotente e
    /// segura entre nós concorrentes, então não há lock de migração como nos providers
    /// relacionais. Em produção, recomenda-se <c>false</c> + criação no pipeline.
    /// </summary>
    public bool AutoMigrate { get; set; } = true;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                "MongoStorageOptions.ConnectionString é obrigatória. Informe-a em " +
                "UseMongoStorage(...) ou na seção Guara:Storage:Mongo:ConnectionString.");
        }

        // O prefixo compõe o nome da coleção: só identificadores estritos passam.
        if (!Prefix().IsMatch(CollectionPrefix))
        {
            throw new InvalidOperationException(
                $"MongoStorageOptions.CollectionPrefix inválido: '{CollectionPrefix}'. Use apenas letras " +
                "minúsculas, dígitos e '_', começando por letra ou '_' (máx. 40 caracteres).");
        }
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,39}$")]
    private static partial Regex Prefix();
}
