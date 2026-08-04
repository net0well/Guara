using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Guara.Abstractions;
using Microsoft.AspNetCore.WebUtilities;

namespace Guara.Dashboard;

/// <summary>
/// Sessão do login fixo: cookie HMAC-SHA256 assinado com validade — sem estado no
/// servidor. Segredo configurável (necessário atrás de load balancer); sem segredo,
/// uma chave aleatória por boot invalida sessões no restart.
/// </summary>
internal sealed class DashboardSessionService(DashboardOptions options, TimeProvider time)
{
    /// <summary>Nome do cookie de sessão.</summary>
    public const string CookieName = "guara.dashboard";

    private readonly byte[] _key = options.CookieSecret is { Length: > 0 } secret
        ? SHA256.HashData(Encoding.UTF8.GetBytes(secret))
        : RandomNumberGenerator.GetBytes(32);

    public string Issue(string usuario)
    {
        var expires = time.GetUtcNow() + options.SessionTtl;
        var payload = Encoding.UTF8.GetBytes($"{usuario}|{expires.UtcTicks}");
        var signature = HMACSHA256.HashData(_key, payload);
        return $"{WebEncoders.Base64UrlEncode(payload)}.{WebEncoders.Base64UrlEncode(signature)}";
    }

    public ClaimsPrincipal? Validate(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length != 2)
        {
            return null;
        }

        byte[] payload;
        byte[] signature;
        try
        {
            payload = WebEncoders.Base64UrlDecode(parts[0]);
            signature = WebEncoders.Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_key, payload), signature))
        {
            return null;
        }

        var content = Encoding.UTF8.GetString(payload);
        var separator = content.LastIndexOf('|');
        if (separator <= 0 || !long.TryParse(content[(separator + 1)..], out var expiresTicks)
            || new DateTimeOffset(expiresTicks, TimeSpan.Zero) <= time.GetUtcNow())
        {
            return null;
        }

        var identity = new ClaimsIdentity(authenticationType: "GuaraDashboard");
        identity.AddClaim(new Claim(ClaimTypes.Name, content[..separator]));

        // O login fixo é o dono do painel daquela instalação: recebe todas as permissões.
        // Sem isto, ligar AddGuaraAuthorization() trancaria o próprio administrador para
        // fora, já que essa identidade não vem de um provedor de claims do host.
        foreach (var action in GuaraActions.All)
        {
            identity.AddClaim(new Claim(GuaraClaimTypes.Permission, action));
        }

        return new ClaimsPrincipal(identity);
    }
}

/// <summary>
/// Rate limiting do login fixo por IP: janela deslizante de falhas + lockout —
/// tentativas de força bruta ficam caras sem afetar quem digita certo.
/// </summary>
internal sealed class LoginRateLimiter(TimeProvider time)
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public bool IsLocked(string ip)
    {
        if (!_entries.TryGetValue(ip, out var entry))
        {
            return false;
        }

        if (entry.LockedUntil is { } until && until > time.GetUtcNow())
        {
            return true;
        }

        return false;
    }

    public void RegisterFailure(string ip)
    {
        var now = time.GetUtcNow();
        _entries.AddOrUpdate(
            ip,
            _ => new Entry(1, now, null),
            (_, entry) =>
            {
                var count = now - entry.WindowStart > Window ? 1 : entry.Failures + 1;
                var windowStart = count == 1 ? now : entry.WindowStart;
                return count >= MaxFailures
                    ? new Entry(count, windowStart, now + Lockout)
                    : new Entry(count, windowStart, null);
            });
    }

    public void RegisterSuccess(string ip) => _entries.TryRemove(ip, out _);

    private sealed record Entry(int Failures, DateTimeOffset WindowStart, DateTimeOffset? LockedUntil);
}
