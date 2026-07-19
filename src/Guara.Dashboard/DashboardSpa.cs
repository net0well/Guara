using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Guara.Dashboard;

/// <summary>
/// SPA Angular embutida como recursos do assembly (prefixo <c>Guara.Dashboard.Spa.</c>),
/// produzida pelo build de <c>Guara.Dashboard.Angular</c>. Quando o build não rodou
/// (sem Node), <see cref="Available"/> é <c>false</c> e a composição serve a página
/// placeholder. O <c>&lt;base href&gt;</c> do index é reescrito para o BasePath real —
/// os demais assets são referenciados de forma relativa a ele.
/// </summary>
internal sealed partial class DashboardSpa
{
    private const string ResourcePrefix = "Guara.Dashboard.Spa.";
    private const string IndexFile = "index.html";

    private readonly Dictionary<string, byte[]> _assets = new(StringComparer.OrdinalIgnoreCase);
    private readonly byte[]? _indexTemplate;

    public DashboardSpa()
    {
        var assembly = typeof(DashboardSpa).Assembly;
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var file = name[ResourcePrefix.Length..].Replace('\\', '/');
            var bytes = Read(assembly, name);
            if (string.Equals(file, IndexFile, StringComparison.OrdinalIgnoreCase))
            {
                _indexTemplate = bytes;
            }
            else
            {
                _assets[file] = bytes;
            }
        }

        Available = _indexTemplate is not null;
    }

    /// <summary>A SPA foi embutida (o build rodou) e pode ser servida.</summary>
    public bool Available { get; }

    /// <summary>Bytes de um asset (js/css/…) pelo caminho relativo, ou <c>null</c>.</summary>
    public byte[]? Asset(string path) => _assets.TryGetValue(path, out var bytes) ? bytes : null;

    /// <summary>index.html com o <c>&lt;base href&gt;</c> ajustado ao BasePath real.</summary>
    public byte[] Index(string basePath)
    {
        var html = Encoding.UTF8.GetString(_indexTemplate!);
        html = BaseHrefRegex().Replace(html, $"<base href=\"{basePath}/\">", 1);
        return Encoding.UTF8.GetBytes(html);
    }

    /// <summary>Content-type por extensão (conjunto que a SPA produz).</summary>
    public static string ContentType(string path)
    {
        var dot = path.LastIndexOf('.');
        var ext = dot < 0 ? "" : path[dot..].ToLowerInvariant();
        return ext switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" or ".mjs" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" or ".map" => "application/json; charset=utf-8",
            ".webmanifest" => "application/manifest+json; charset=utf-8",
            ".ico" => "image/x-icon",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream",
        };
    }

    private static byte[] Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    [GeneratedRegex("<base href=\"[^\"]*\">")]
    private static partial Regex BaseHrefRegex();
}
