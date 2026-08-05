namespace Guara.Storage.MySql;

/// <summary>
/// Conversão de instantes para o MySQL. Não existe tipo com fuso no MySQL — <c>TIMESTAMP</c>
/// ainda estoura em 2038 e <c>DATETIME</c> não carrega offset — então tudo é gravado em UTC
/// e volta como UTC. A precisão é de microssegundo, a mesma do PostgreSQL.
/// </summary>
internal static class MySqlTime
{
    /// <summary>Converte para o valor gravado na coluna <c>DATETIME(6)</c>.</summary>
    public static DateTime ToDatabase(DateTimeOffset value) => value.UtcDateTime;

    /// <summary>Converte para o valor opcional gravado na coluna, ou <see cref="DBNull"/>.</summary>
    public static object ToDatabaseOrNull(DateTimeOffset? value)
        => value is { } instante ? instante.UtcDateTime : DBNull.Value;

    /// <summary>Reconstrói o instante lido da coluna, sempre em UTC.</summary>
    public static DateTimeOffset FromDatabase(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
