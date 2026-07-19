using System.Text.Encodings.Web;

namespace Guara.Dashboard;

/// <summary>
/// Páginas HTML embutidas (login e placeholder da UI): identidade do Guará, tema
/// claro/escuro pelo sistema, pt-BR/en pelo Accept-Language, acessíveis e sem nenhum
/// recurso externo. A SPA (Guara.Dashboard.Angular) substitui o placeholder.
/// </summary>
internal static class DashboardPages
{
    private static readonly HtmlEncoder Html = HtmlEncoder.Default;

    internal static bool PrefersEnglish(string? acceptLanguage)
        => acceptLanguage is not null
           && acceptLanguage.Split(',')[0].TrimStart().StartsWith("en", StringComparison.OrdinalIgnoreCase);

    public static string Login(
        string basePath, string? retorno, string? erro, bool fixedLoginEnabled, bool english)
    {
        var t = english
            ? (Titulo: "Sign in — Guará", Entrar: "Sign in", Usuario: "Username", Senha: "Password",
               Slogan: "Job scheduling, made in Brasil.",
               ErroCredenciais: "Invalid username or password.",
               ErroBloqueado: "Too many attempts — wait a few minutes.",
               SemLoginFixo: "This dashboard uses the host application's authentication. Sign in to the application and come back.")
            : (Titulo: "Entrar — Guará", Entrar: "Entrar", Usuario: "Usuário", Senha: "Senha",
               Slogan: "Agendamento de jobs, made in Brasil.",
               ErroCredenciais: "Usuário ou senha inválidos.",
               ErroBloqueado: "Muitas tentativas — aguarde alguns minutos.",
               SemLoginFixo: "Este dashboard usa a autenticação da aplicação. Autentique-se na aplicação e volte.");

        var mensagemErro = erro switch
        {
            "credenciais" => $"<p class=\"erro\" role=\"alert\">{t.ErroCredenciais}</p>",
            "bloqueado" => $"<p class=\"erro\" role=\"alert\">{t.ErroBloqueado}</p>",
            _ => "",
        };

        var corpo = fixedLoginEnabled
            ? $"""
              {mensagemErro}
              <form method="post" action="{Html.Encode(basePath)}/login">
                  <input type="hidden" name="retorno" value="{Html.Encode(retorno ?? "")}" />
                  <label for="usuario">{t.Usuario}</label>
                  <input id="usuario" name="usuario" autocomplete="username" required autofocus />
                  <label for="senha">{t.Senha}</label>
                  <input id="senha" name="senha" type="password" autocomplete="current-password" required />
                  <button type="submit">{t.Entrar}</button>
              </form>
              """
            : $"<p class=\"aviso\">{t.SemLoginFixo}</p>";

        return $$"""
            <!DOCTYPE html>
            <html lang="{{(english ? "en" : "pt-BR")}}">
            <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <meta name="color-scheme" content="light dark" />
            <title>{{t.Titulo}}</title>
            <style>
            :root { --fundo:#faf6f1; --cartao:#ffffff; --texto:#2b211c; --suave:#8a7466;
                    --marca:#c2451e; --marca-escura:#9a3315; --borda:#e8ddd3; --erro:#b3261e; }
            @media (prefers-color-scheme: dark) {
              :root { --fundo:#191210; --cartao:#241a16; --texto:#f3e9e2; --suave:#b3998a;
                      --marca:#e0592b; --marca-escura:#c2451e; --borda:#3b2c24; --erro:#ffb4ab; }
            }
            * { box-sizing:border-box; }
            body { margin:0; min-height:100vh; display:grid; place-items:center;
                   background:radial-gradient(1200px 600px at 20% -10%, color-mix(in srgb, var(--marca) 14%, var(--fundo)), var(--fundo));
                   color:var(--texto); font:16px/1.5 system-ui, "Segoe UI", sans-serif; }
            main { width:min(92vw, 380px); background:var(--cartao); border:1px solid var(--borda);
                   border-radius:16px; padding:2rem; box-shadow:0 18px 50px rgb(0 0 0 / .12); }
            .logo { display:block; width:96px; height:96px; margin:0 auto .5rem; border-radius:20px; }
            h1 { margin:.25rem 0 0; text-align:center; font-size:1.6rem; letter-spacing:.02em; }
            .slogan { margin:.2rem 0 1.5rem; text-align:center; color:var(--suave); font-size:.9rem; }
            label { display:block; margin:.9rem 0 .3rem; font-weight:600; font-size:.9rem; }
            input { width:100%; padding:.65rem .8rem; border-radius:10px; border:1px solid var(--borda);
                    background:transparent; color:inherit; font:inherit; }
            input:focus-visible { outline:2px solid var(--marca); outline-offset:1px; }
            button { width:100%; margin-top:1.4rem; padding:.7rem; border:0; border-radius:10px;
                     background:linear-gradient(180deg, var(--marca), var(--marca-escura));
                     color:#fff; font:inherit; font-weight:700; cursor:pointer; }
            button:hover { filter:brightness(1.07); }
            .erro { color:var(--erro); text-align:center; margin:0 0 .5rem; font-size:.9rem; }
            .aviso { color:var(--suave); text-align:center; }
            </style>
            </head>
            <body>
            <main>
                <img class="logo" src="{{Html.Encode(basePath)}}/assets/logo.png" alt="Guará" />
                <h1>Guará</h1>
                <p class="slogan">{{t.Slogan}}</p>
                {{corpo}}
            </main>
            </body>
            </html>
            """;
    }

    public static string Placeholder(string basePath, bool english)
    {
        var t = english
            ? (Titulo: "Guará — dashboard", Texto: "The Angular panel arrives in the next phase. The API is live:",
               Sair: "Sign out")
            : (Titulo: "Guará — painel", Texto: "O painel Angular chega na próxima fase. A API já está no ar:",
               Sair: "Sair");

        return $$"""
            <!DOCTYPE html>
            <html lang="{{(english ? "en" : "pt-BR")}}">
            <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <meta name="color-scheme" content="light dark" />
            <title>{{t.Titulo}}</title>
            <style>
            :root { --fundo:#faf6f1; --texto:#2b211c; --marca:#c2451e; --suave:#8a7466; }
            @media (prefers-color-scheme: dark) { :root { --fundo:#191210; --texto:#f3e9e2; --marca:#e0592b; --suave:#b3998a; } }
            body { margin:0; min-height:100vh; display:grid; place-items:center; background:var(--fundo);
                   color:var(--texto); font:16px/1.6 system-ui, "Segoe UI", sans-serif; text-align:center; }
            img { width:120px; border-radius:24px; }
            a { color:var(--marca); font-weight:600; }
            .sair { display:inline-block; margin-top:1.2rem; color:var(--suave); font-size:.9rem; }
            code { background:rgb(127 127 127 / .12); padding:.1rem .4rem; border-radius:6px; }
            </style>
            </head>
            <body>
            <main>
                <img src="{{Html.Encode(basePath)}}/assets/logo.png" alt="Guará" />
                <h1>Guará</h1>
                <p>{{t.Texto}}</p>
                <p><a href="{{Html.Encode(basePath)}}/api/v1/stats">{{Html.Encode(basePath)}}/api/v1/stats</a></p>
                <p><code>GET {{Html.Encode(basePath)}}/api/v1/stream</code> — Server-Sent Events</p>
                <a class="sair" href="{{Html.Encode(basePath)}}/logout">{{t.Sair}}</a>
            </main>
            </body>
            </html>
            """;
    }
}
