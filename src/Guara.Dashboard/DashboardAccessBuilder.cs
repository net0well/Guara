using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Guara.Dashboard;

/// <summary>
/// Configuração fluente das regras de acesso ao dashboard (métodos em português).
/// As regras combinam em <b>E</b> — todas precisam passar; use
/// <see cref="QualquerUma"/> para um grupo <b>OU</b>.
/// </summary>
public sealed class DashboardAccessBuilder
{
    private readonly List<Func<IServiceProvider, IDashboardAccessRule>> _rules = [];
    private readonly bool _isGroup;
    private FixedLoginOptions? _fixedLogin;
    private bool _requiresAuthenticated;

    internal DashboardAccessBuilder(bool isGroup = false) => _isGroup = isGroup;

    /// <summary>Exige usuário autenticado (identidade do host ou sessão do login fixo).</summary>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public DashboardAccessBuilder PermitirApenasLogados()
    {
        if (!_requiresAuthenticated)
        {
            _requiresAuthenticated = true;
            _rules.Add(static _ => new AuthenticatedRule());
        }

        return this;
    }

    /// <summary>Exige ao menos um dos papéis informados.</summary>
    /// <param name="papeis">Papéis aceitos.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public DashboardAccessBuilder ExigirPapel(params string[] papeis)
    {
        if (papeis is not { Length: > 0 } || papeis.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Informe ao menos um papel não vazio.", nameof(papeis));
        }

        _rules.Add(_ => new RoleRule(papeis));
        return this;
    }

    /// <summary>Exige uma claim (com valor exato, quando informado).</summary>
    /// <param name="tipo">Tipo da claim.</param>
    /// <param name="valor">Valor exigido; nulo aceita qualquer valor.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public DashboardAccessBuilder ExigirClaim(string tipo, string? valor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);
        _rules.Add(_ => new ClaimRule(tipo, valor));
        return this;
    }

    /// <summary>Permite apenas faixas privadas (10/8, 172.16/12, 192.168/16, fc00::/7) e loopback.</summary>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public DashboardAccessBuilder PermitirApenasIpsInternos()
    {
        _rules.Add(static _ => new InternalIpsRule());
        return this;
    }

    /// <summary>Permite apenas os IPs/faixas CIDR informados (validados nesta chamada).</summary>
    /// <param name="faixas">IPs exatos ou CIDR (ex.: <c>"10.0.0.0/8"</c>).</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public DashboardAccessBuilder PermitirIps(params string[] faixas)
    {
        if (faixas is not { Length: > 0 })
        {
            throw new ArgumentException("Informe ao menos uma faixa de IP.", nameof(faixas));
        }

        var parsed = faixas.Select(CidrRange.Parse).ToArray();
        _rules.Add(_ => new CidrRule(parsed));
        return this;
    }

    /// <summary>
    /// Habilita o login fixo (cenários pequenos/rede interna): a página de login
    /// embutida autentica e emite cookie assinado. A senha é transformada em hash
    /// PBKDF2 <b>nesta chamada</b> — nunca é retida nem logada em claro. Implica
    /// <see cref="PermitirApenasLogados"/>.
    /// </summary>
    /// <param name="usuario">Nome de usuário.</param>
    /// <param name="senha">Senha (venha de env/secret store — nunca literal no código).</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public DashboardAccessBuilder ComLoginFixo(string usuario, string senha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(usuario);
        ArgumentException.ThrowIfNullOrWhiteSpace(senha);
        if (_isGroup)
        {
            throw new InvalidOperationException("ComLoginFixo não pode ficar dentro de QualquerUma(...).");
        }

        _fixedLogin = FixedLoginOptions.Create(usuario, senha);
        return PermitirApenasLogados();
    }

    /// <summary>Registra uma regra customizada resolvida com dependências do DI.</summary>
    /// <typeparam name="TRegra">Tipo da regra.</typeparam>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public DashboardAccessBuilder ComRegra<TRegra>()
        where TRegra : class, IDashboardAccessRule
    {
        _rules.Add(static services => ActivatorUtilities.CreateInstance<TRegra>(services));
        return this;
    }

    /// <summary>Registra uma instância de regra customizada.</summary>
    /// <param name="regra">A regra.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public DashboardAccessBuilder ComRegra(IDashboardAccessRule regra)
    {
        ArgumentNullException.ThrowIfNull(regra);
        _rules.Add(_ => regra);
        return this;
    }

    /// <summary>Grupo OU: a requisição passa se <b>uma</b> das regras do grupo passar.</summary>
    /// <param name="grupo">Configuração das regras alternativas.</param>
    /// <returns>O próprio builder, para encadeamento fluente.</returns>
    public DashboardAccessBuilder QualquerUma(Action<DashboardAccessBuilder> grupo)
    {
        ArgumentNullException.ThrowIfNull(grupo);
        var inner = new DashboardAccessBuilder(isGroup: true);
        grupo(inner);
        if (inner._rules.Count == 0)
        {
            throw new InvalidOperationException("QualquerUma(...) precisa de ao menos uma regra.");
        }

        var factories = inner._rules.ToArray();
        _rules.Add(services => new AnyOfRule([.. factories.Select(factory => factory(services))]));
        return this;
    }

    internal DashboardAccessOptions Build()
    {
        if (_rules.Count == 0)
        {
            throw new InvalidOperationException(
                "UseGuaraAuthentication foi chamado sem nenhuma regra — configure ao menos uma " +
                "(ex.: PermitirApenasLogados() ou ComLoginFixo(...)).");
        }

        return new DashboardAccessOptions([.. _rules], _fixedLogin);
    }
}

/// <summary>Regras de acesso materializadas.</summary>
internal sealed record DashboardAccessOptions(
    IReadOnlyList<Func<IServiceProvider, IDashboardAccessRule>> Rules,
    FixedLoginOptions? FixedLogin);

/// <summary>Credenciais do login fixo — só o hash PBKDF2 fica em memória.</summary>
internal sealed class FixedLoginOptions
{
    private const int Iterations = 100_000;
    private const int HashSize = 32;

    private FixedLoginOptions(string usuario, byte[] salt, byte[] hash)
    {
        Usuario = usuario;
        Salt = salt;
        Hash = hash;
    }

    public string Usuario { get; }

    public byte[] Salt { get; }

    public byte[] Hash { get; }

    public static FixedLoginOptions Create(string usuario, string senha)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(senha), salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return new FixedLoginOptions(usuario, salt, hash);
    }

    public bool Validate(string usuario, string senha)
    {
        var candidate = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(senha), Salt, Iterations, HashAlgorithmName.SHA256, HashSize);

        // Comparações em tempo constante: nem usuário nem senha vazam por timing.
        var userMatches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(usuario.PadRight(64)[..64]),
            Encoding.UTF8.GetBytes(Usuario.PadRight(64)[..64]));
        return CryptographicOperations.FixedTimeEquals(candidate, Hash) && userMatches;
    }
}
