using System.Net;
using System.Net.Sockets;

namespace Guara.Dashboard;

/// <summary>Exige identidade autenticada (host ou sessão do login fixo).</summary>
internal sealed class AuthenticatedRule : IDashboardAccessRule
{
    public ValueTask<bool> AutorizarAsync(DashboardContext contexto, CancellationToken ct)
        => ValueTask.FromResult(contexto.User.Identity?.IsAuthenticated == true);
}

/// <summary>Exige autenticação e ao menos um dos papéis informados.</summary>
internal sealed class RoleRule(string[] papeis) : IDashboardAccessRule
{
    public ValueTask<bool> AutorizarAsync(DashboardContext contexto, CancellationToken ct)
        => ValueTask.FromResult(
            contexto.User.Identity?.IsAuthenticated == true && papeis.Any(contexto.User.IsInRole));
}

/// <summary>Exige uma claim (opcionalmente com valor exato).</summary>
internal sealed class ClaimRule(string tipo, string? valor) : IDashboardAccessRule
{
    public ValueTask<bool> AutorizarAsync(DashboardContext contexto, CancellationToken ct)
        => ValueTask.FromResult(valor is null
            ? contexto.User.HasClaim(claim => claim.Type == tipo)
            : contexto.User.HasClaim(tipo, valor));
}

/// <summary>Permite apenas faixas privadas (RFC 1918/4193) e loopback.</summary>
internal sealed class InternalIpsRule : IDashboardAccessRule
{
    public ValueTask<bool> AutorizarAsync(DashboardContext contexto, CancellationToken ct)
        => ValueTask.FromResult(contexto.RemoteIp is { } ip && IsInternal(Normalize(ip)));

    private static IPAddress Normalize(IPAddress ip)
        => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    private static bool IsInternal(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168);
        }

        // IPv6: fc00::/7 (unique local) e fe80::/10 (link-local).
        var first = ip.GetAddressBytes()[0];
        return (first & 0xFE) == 0xFC || (first == 0xFE && (ip.GetAddressBytes()[1] & 0xC0) == 0x80);
    }
}

/// <summary>Permite apenas os IPs/faixas CIDR informados.</summary>
internal sealed class CidrRule(CidrRange[] faixas) : IDashboardAccessRule
{
    public ValueTask<bool> AutorizarAsync(DashboardContext contexto, CancellationToken ct)
    {
        if (contexto.RemoteIp is not { } ip)
        {
            return ValueTask.FromResult(false);
        }

        var normalized = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
        return ValueTask.FromResult(faixas.Any(faixa => faixa.Contains(normalized)));
    }
}

/// <summary>Faixa CIDR (<c>10.0.0.0/8</c>, <c>2001:db8::/32</c> ou IP exato).</summary>
internal readonly struct CidrRange(IPAddress network, int prefixLength)
{
    public static CidrRange Parse(string texto)
    {
        var parts = texto.Split('/');
        if (!IPAddress.TryParse(parts[0], out var network) || parts.Length > 2
            || (parts.Length == 2 && !int.TryParse(parts[1], out _)))
        {
            throw new InvalidOperationException(
                $"Faixa de IP inválida: '{texto}'. Use um IP exato ou notação CIDR (ex.: 10.0.0.0/8).");
        }

        var maxPrefix = network.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var prefix = parts.Length == 2 ? int.Parse(parts[1]) : maxPrefix;
        if (prefix < 0 || prefix > maxPrefix)
        {
            throw new InvalidOperationException(
                $"Prefixo CIDR inválido em '{texto}': use 0..{maxPrefix}.");
        }

        return new CidrRange(network, prefix);
    }

    public bool Contains(IPAddress ip)
    {
        if (ip.AddressFamily != network.AddressFamily)
        {
            return false;
        }

        var address = ip.GetAddressBytes();
        var expected = network.GetAddressBytes();
        var fullBytes = prefixLength / 8;
        for (var i = 0; i < fullBytes; i++)
        {
            if (address[i] != expected[i])
            {
                return false;
            }
        }

        var remainder = prefixLength % 8;
        if (remainder == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - remainder));
        return (address[fullBytes] & mask) == (expected[fullBytes] & mask);
    }
}

/// <summary>Grupo OU: basta uma das regras internas passar.</summary>
internal sealed class AnyOfRule(IReadOnlyList<IDashboardAccessRule> regras) : IDashboardAccessRule
{
    public async ValueTask<bool> AutorizarAsync(DashboardContext contexto, CancellationToken ct)
    {
        foreach (var regra in regras)
        {
            if (await regra.AutorizarAsync(contexto, ct))
            {
                return true;
            }
        }

        return false;
    }
}
