using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Guara.SourceGenerators;

/// <summary>
/// Array imutável com igualdade por conteúdo — obrigatório nos modelos do pipeline
/// incremental para o cache funcionar (ImmutableArray compara por referência).
/// </summary>
/// <typeparam name="T">Tipo do item (equatable).</typeparam>
public readonly struct EquatableArray<T>(ImmutableArray<T> items) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _items = items;

    /// <summary>Itens do array (vazio quando default).</summary>
    public ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    /// <summary>Quantidade de itens.</summary>
    public int Count => Items.Length;

    /// <inheritdoc />
    public bool Equals(EquatableArray<T> other) => Items.SequenceEqual(other.Items);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in Items)
            {
                hash = (hash * 31) + (item?.GetHashCode() ?? 0);
            }

            return hash;
        }
    }

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Parâmetro de um método de job, com os trechos de serialização já resolvidos.</summary>
/// <param name="Name">Nome do parâmetro.</param>
/// <param name="FullTypeName">Tipo totalmente qualificado (<c>global::...</c>).</param>
/// <param name="Kind">Como serializar (mapa de tipos suportados).</param>
/// <param name="IsCancellationToken">É o <c>CancellationToken</c> final (não serializado).</param>
/// <param name="WriterStatement">Statement completo que escreve <c>@nome</c> no <c>writer</c>.</param>
/// <param name="ReaderExpression">Expressão completa que lê o valor do <c>reader</c>.</param>
public sealed record ParameterModel(
    string Name,
    string FullTypeName,
    ArgKind Kind,
    bool IsCancellationToken,
    string WriterStatement = "",
    string ReaderExpression = "");

/// <summary>Categorias de serialização suportadas para argumentos de job.</summary>
public enum ArgKind
{
    /// <summary>Tipo fora do conjunto suportado (gera diagnóstico).</summary>
    Unsupported,

    /// <summary>Números inteiros e de ponto flutuante (escrita numérica direta).</summary>
    Number,

    /// <summary>Booleano.</summary>
    Boolean,

    /// <summary>String.</summary>
    String,

    /// <summary>Guid.</summary>
    Guid,

    /// <summary>DateTime.</summary>
    DateTime,

    /// <summary>DateTimeOffset.</summary>
    DateTimeOffset,

    /// <summary>TimeSpan (formato invariante "c").</summary>
    TimeSpan,

    /// <summary>DateOnly (ISO).</summary>
    DateOnly,

    /// <summary>TimeOnly (ISO).</summary>
    TimeOnly,

    /// <summary>Char (string de um caractere).</summary>
    Char,

    /// <summary>Uri (string absoluta/relativa).</summary>
    Uri,

    /// <summary>Enum (valor inteiro subjacente).</summary>
    Enum,
}

/// <summary>Posição de um diagnóstico (equatable — <c>Location</c> do Roslyn não é).</summary>
/// <param name="FilePath">Arquivo.</param>
/// <param name="Start">Início do span.</param>
/// <param name="Length">Tamanho do span.</param>
/// <param name="StartLine">Linha inicial (0-based).</param>
/// <param name="StartCharacter">Coluna inicial (0-based).</param>
/// <param name="EndLine">Linha final (0-based).</param>
/// <param name="EndCharacter">Coluna final (0-based).</param>
public sealed record LocationModel(
    string FilePath, int Start, int Length, int StartLine, int StartCharacter, int EndLine, int EndCharacter);

/// <summary>Diagnóstico coletado pelo parser, com os argumentos da mensagem.</summary>
/// <param name="Id">Id do descriptor (<c>GUARA01xx</c>).</param>
/// <param name="Location">Onde reportar.</param>
/// <param name="MessageArgs">Argumentos da mensagem.</param>
public sealed record DiagnosticModel(string Id, LocationModel? Location, EquatableArray<string> MessageArgs);

/// <summary>Um método <c>[GuaraJob]</c> descoberto, já com os atributos resolvidos.</summary>
public sealed record JobModel
{
    /// <summary>Discriminador estável persistido no descriptor (namespace + tipo, sem assembly).</summary>
    public required string TypeName { get; init; }

    /// <summary>Tipo que contém o método (<c>global::...</c>).</summary>
    public required string TypeFullName { get; init; }

    /// <summary>Nome simples da classe (para nomear a factory gerada).</summary>
    public required string TypeShortName { get; init; }

    /// <summary>Nome do método.</summary>
    public required string MethodName { get; init; }

    /// <summary>Como o método retorna (<c>Task</c>/<c>ValueTask</c>/<c>void</c>).</summary>
    public required ReturnKind ReturnKind { get; init; }

    /// <summary>Método estático (não resolve a classe no DI).</summary>
    public required bool IsStatic { get; init; }

    /// <summary>Parâmetros na ordem declarada (inclui o CancellationToken final, se houver).</summary>
    public required EquatableArray<ParameterModel> Parameters { get; init; }

    /// <summary>Fila declarada em <c>[GuaraFila]</c>.</summary>
    public string? Queue { get; init; }

    /// <summary>Retentativas máximas declaradas em <c>[GuaraRetentativas]</c>.</summary>
    public int? MaxAttempts { get; init; }

    /// <summary>Tempo limite declarado em <c>[GuaraTempoLimite]</c>.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Declarado <c>[GuaraPularSeAnteriorEmExecucao]</c>.</summary>
    public bool SkipIfPreviousRunning { get; init; }

    /// <summary>Declarado <c>[GuaraDesabilitarConcorrencia]</c>.</summary>
    public bool DisableConcurrency { get; init; }

    /// <summary>Template da chave de exclusão mútua (placeholders <c>{0}</c> já validados).</summary>
    public string? ConcurrencyKeyTemplate { get; init; }

    /// <summary>Espera máxima pela chave antes de devolver o job à fila.</summary>
    public int ConcurrencyWaitSeconds { get; init; }

    /// <summary>Diagnósticos a reportar para este método.</summary>
    public EquatableArray<DiagnosticModel> Diagnostics { get; init; }

    /// <summary>Posição do método (para diagnósticos de nível de emissão, ex.: duplicado).</summary>
    public LocationModel? Location { get; init; }

    /// <summary>Parâmetros serializáveis (sem o CancellationToken).</summary>
    public IEnumerable<ParameterModel> SerializableParameters
        => Parameters.Items.Where(parameter => !parameter.IsCancellationToken);

    /// <summary>Identificador seguro para nomes de tipos gerados.</summary>
    public string SafeName => $"{TypeShortName}_{MethodName}";
}

/// <summary>Forma de retorno do método de job.</summary>
public enum ReturnKind
{
    /// <summary><c>Task</c>.</summary>
    Task,

    /// <summary><c>ValueTask</c>.</summary>
    ValueTask,

    /// <summary><c>void</c> (síncrono).</summary>
    Void,
}
