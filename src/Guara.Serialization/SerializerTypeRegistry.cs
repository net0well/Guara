namespace Guara.Serialization;

/// <summary>
/// Allowlist de tipos serializáveis: mapa bidirecional <c>discriminador ⇄ Type</c>.
/// A desserialização de argumentos só resolve tipos registrados aqui — nunca a partir
/// de nomes qualificados vindos do payload (por segurança).
/// Registre durante o bootstrap; leituras posteriores são thread-safe.
/// No futuro, o registro será preenchido em compilação pelo <c>Guara.SourceGenerators</c>.
/// </summary>
internal sealed class SerializerTypeRegistry
{
    private readonly Dictionary<string, Type> _byDiscriminator = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> _byType = [];

    /// <summary>Registra um tipo na allowlist.</summary>
    /// <typeparam name="T">Tipo a registrar.</typeparam>
    /// <param name="discriminator">Discriminador curto e estável (nunca nome de assembly).</param>
    /// <returns>O próprio registry, para encadeamento fluente.</returns>
    public SerializerTypeRegistry Register<T>(string discriminator) => Register(discriminator, typeof(T));

    /// <summary>Registra um tipo na allowlist.</summary>
    /// <param name="discriminator">Discriminador curto e estável (nunca nome de assembly).</param>
    /// <param name="type">Tipo a registrar.</param>
    /// <returns>O próprio registry, para encadeamento fluente.</returns>
    /// <exception cref="ArgumentException">Discriminador vazio ou já registrado para outro tipo.</exception>
    public SerializerTypeRegistry Register(string discriminator, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discriminator);
        ArgumentNullException.ThrowIfNull(type);

        if (_byDiscriminator.TryGetValue(discriminator, out var existing) && existing != type)
        {
            throw new ArgumentException(
                $"O discriminador '{discriminator}' já está registrado para '{existing}'.", nameof(discriminator));
        }

        _byDiscriminator[discriminator] = type;
        _byType[type] = discriminator;
        return this;
    }

    /// <summary>Resolve o tipo de um discriminador, se registrado.</summary>
    /// <param name="discriminator">Discriminador presente no payload.</param>
    /// <param name="type">Tipo correspondente, quando registrado.</param>
    /// <returns><c>true</c> se o discriminador está na allowlist.</returns>
    public bool TryGetType(string discriminator, out Type type)
    {
        if (_byDiscriminator.TryGetValue(discriminator, out var found))
        {
            type = found;
            return true;
        }

        type = null!;
        return false;
    }

    /// <summary>Resolve o discriminador de um tipo, se registrado.</summary>
    /// <param name="type">Tipo a resolver.</param>
    /// <param name="discriminator">Discriminador correspondente, quando registrado.</param>
    /// <returns><c>true</c> se o tipo está na allowlist.</returns>
    public bool TryGetDiscriminator(Type type, out string discriminator)
    {
        if (_byType.TryGetValue(type, out var found))
        {
            discriminator = found;
            return true;
        }

        discriminator = null!;
        return false;
    }

    /// <summary>
    /// Cria um registry com os tipos primitivos comuns pré-registrados
    /// (argumentos simples de job funcionam sem configuração).
    /// </summary>
    /// <returns>Um registry novo com os primitivos registrados.</returns>
    public static SerializerTypeRegistry CreateDefault() => new SerializerTypeRegistry()
        .Register<string>("string")
        .Register<int>("int")
        .Register<long>("long")
        .Register<bool>("bool")
        .Register<double>("double")
        .Register<decimal>("decimal")
        .Register<DateTimeOffset>("dateTimeOffset")
        .Register<DateTime>("dateTime")
        .Register<TimeSpan>("timeSpan")
        .Register<Guid>("guid");
}
