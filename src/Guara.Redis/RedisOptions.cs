using System.Text.RegularExpressions;

namespace Guara.Redis;

/// <summary>
/// Opções do acelerador Redis. Configuráveis pela seção <c>Guara:Redis</c> (a connection
/// string é um segredo: prefira variável de ambiente/secret store — ela nunca é logada
/// pelo Guará).
/// </summary>
public sealed partial class RedisOptions
{
    /// <summary>
    /// Connection string do Redis. Dispensável quando a aplicação já registra um
    /// <c>IConnectionMultiplexer</c> no contêiner — nesse caso o Guará usa o dela.
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Prefixo dos canais de pub/sub. Isola instalações que dividem o mesmo Redis: dois
    /// ambientes com prefixos diferentes não acordam o dispatcher um do outro.
    /// </summary>
    public string ChannelPrefix { get; set; } = "guara";

    internal void Validate(bool conexaoNoContainer)
    {
        if (!conexaoNoContainer && string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException(
                "RedisOptions.ConnectionString é obrigatória quando não há IConnectionMultiplexer " +
                "registrado no contêiner. Informe-a em UseRedis(...) ou na seção " +
                "Guara:Redis:ConnectionString.");
        }

        // O prefixo compõe o nome do canal: restringir aqui evita que um caractere de
        // padrão (glob) transforme uma publicação dirigida em algo que casa com outros.
        if (!ChannelPrefixName().IsMatch(ChannelPrefix))
        {
            throw new InvalidOperationException(
                $"RedisOptions.ChannelPrefix inválido: '{ChannelPrefix}'. Use apenas letras, dígitos, " +
                "'_', '-' e ':' (1 a 64 caracteres).");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_:-]{1,64}$")]
    private static partial Regex ChannelPrefixName();
}
