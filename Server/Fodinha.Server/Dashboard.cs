using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Fodinha.Server
{
    /// <summary>
    /// Senha do painel (HTTP Basic, só por HTTPS). Erra demais = bloqueio por IP e geral, contra chute.
    /// Sem senha configurada (ou curta demais), o painel nem existe (404).
    /// </summary>
    public sealed class AdminGate
    {
        public const int MinKeyLength = 12;
        public const int MaxFailuresPerAddress = 10;   // por hora
        public const int MaxFailuresGlobal = 100;      // por hora, somando todos os IPs

        public enum Result { Ok, Missing, Denied, Locked, Disabled }

        private readonly object gate = new object();
        private readonly Dictionary<string, SlidingCounter> failures = new Dictionary<string, SlidingCounter>();
        private readonly SlidingCounter global = new SlidingCounter();
        private readonly Func<double> clock;

        public AdminGate(Func<double> secondsClock = null)
        {
            var sw = Stopwatch.StartNew();
            clock = secondsClock ?? (() => sw.Elapsed.TotalSeconds);
        }

        public Result Check(string key, string authorization, string address)
        {
            if (string.IsNullOrEmpty(key) || key.Length < MinKeyLength) return Result.Disabled;
            double now = clock();
            lock (gate)
            {
                failures.TryGetValue(address ?? "", out var mine);
                if ((mine != null && mine.Count(now, 3600) >= MaxFailuresPerAddress) || global.Count(now, 3600) >= MaxFailuresGlobal)
                    return Result.Locked;
            }
            string given = PasswordFrom(authorization);
            if (given == null) return Result.Missing; // 1º acesso: o navegador ainda não mandou a senha
            if (Same(given, key)) return Result.Ok;
            lock (gate)
            {
                if (!failures.TryGetValue(address ?? "", out var mine)) failures[address ?? ""] = mine = new SlidingCounter();
                mine.Add(now);
                global.Add(now);
                if (failures.Count > 10000)
                    foreach (var k in failures.Where(kv => kv.Value.Count(now, 3600) == 0).Select(kv => kv.Key).ToList()) failures.Remove(k);
            }
            return Result.Denied;
        }

        /// <summary>"Basic base64(usuario:senha)" → senha (o usuário pode ser qualquer um).</summary>
        public static string PasswordFrom(string authorization)
        {
            if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                string pair = Encoding.UTF8.GetString(Convert.FromBase64String(authorization.Substring(6).Trim()));
                int colon = pair.IndexOf(':');
                return colon < 0 ? null : pair.Substring(colon + 1);
            }
            catch (FormatException)
            {
                return "";
            }
        }

        private static bool Same(string a, string b)
        {
            var ha = SHA256.HashData(Encoding.UTF8.GetBytes(a));
            var hb = SHA256.HashData(Encoding.UTF8.GetBytes(b));
            return CryptographicOperations.FixedTimeEquals(ha, hb);
        }
    }

    /// <summary>Conta as visitas ao site (abrir a página do jogo). Robôs e pré-visualizações de link não contam.</summary>
    public static class VisitCounter
    {
        public static bool IsPage(HttpRequest r) =>
            HttpMethods.IsGet(r.Method) && (r.Path == "/" || r.Path == "/index.html");

        public static void Count(StatsBook stats, HttpContext ctx, bool trustProxy)
        {
            var r = ctx.Request;
            string ua = r.Headers.UserAgent.ToString();
            if (Metric.IsRobot(ua)) return;
            // Navegador "adiantando" a página (prefetch) não é visita.
            if (r.Headers["Sec-Purpose"].ToString().Contains("prefetch") || r.Headers["Purpose"].ToString().Contains("prefetch")) return;

            string device = Metric.DeviceOf(ua);
            string ip = ClientAddress.Resolve(name => r.Headers[name], ctx.Connection.RemoteIpAddress, trustProxy);
            string visitor = stats.Hash(ip);

            stats.Add(Metric.Visits);
            stats.Add(Metric.Visits + "_" + device);
            if (stats.FirstToday(Metric.Visitors, visitor))
            {
                stats.Add(Metric.Visitors);
                stats.Add(Metric.Visitors + "_" + device);
            }
            if (r.Query["origem"] == "convite")
            {
                stats.Add(Metric.InviteVisits);
                if (stats.FirstToday(Metric.InviteVisitors, visitor)) stats.Add(Metric.InviteVisitors);
            }
        }
    }

    /// <summary>Painel do dono do jogo (/painel): números do dia, últimos 30 dias e cópias de segurança.</summary>
    public static class Dashboard
    {
        public const string Path = "/painel";
        public const long MaxUploadBytes = 64L * 1024 * 1024;

        internal sealed class Now
        {
            public int Connected;
            public int Rooms;
            public int Matches;
            public int Humans;
            public int Accounts;
        }

        public static void Map(WebApplication app, ServerOptions options, bool trustProxy)
        {
            var gate = new AdminGate();

            // Confere a senha. Devolve null se pode seguir; senão, a resposta de recusa.
            IResult Deny(HttpContext ctx)
            {
                ctx.Response.Headers["Cache-Control"] = "no-store";
                ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
                string address = ClientAddress.Resolve(name => ctx.Request.Headers[name], ctx.Connection.RemoteIpAddress, trustProxy);
                switch (gate.Check(options.AdminKey, ctx.Request.Headers.Authorization, address))
                {
                    case AdminGate.Result.Ok: return null;
                    case AdminGate.Result.Disabled: return Results.NotFound();
                    case AdminGate.Result.Locked: return Results.Text("Muitas senhas erradas. Tente de novo daqui a uma hora.", "text/plain; charset=utf-8", null, 429);
                    default:
                        ctx.Response.Headers["WWW-Authenticate"] = "Basic realm=\"Painel do Fodinha\", charset=\"UTF-8\"";
                        return Results.Text("Senha do painel necessária.", "text/plain; charset=utf-8", null, 401);
                }
            }

            app.MapGet(Path, async (HttpContext ctx, GameHost host, StatsBook stats, BackupService backups) =>
            {
                var deny = Deny(ctx);
                if (deny != null) return deny;
                var now = await host.Query(core => new Now
                {
                    Connected = core.ConnectedCount,
                    Rooms = core.RoomCount,
                    Matches = core.MatchesInProgress,
                    Humans = core.HumansInMatches,
                    Accounts = core.Accounts.Count
                });
                List<BackupInfo> list;
                string backupError = backups.LastError;
                try { list = await backups.Store.ListAsync(ctx.RequestAborted); }
                catch (Exception e) { list = new List<BackupInfo>(); backupError = e.Message; }
                string message = ctx.Request.Query["msg"];
                return Results.Content(Page(stats, now, list, backupError, message), "text/html; charset=utf-8");
            });

            app.MapGet(Path + "/dados.json", (HttpContext ctx, StatsBook stats) =>
            {
                var deny = Deny(ctx);
                if (deny != null) return deny;
                var days = stats.Recent(90).Select(d => new { dia = d.Day.ToString("yyyy-MM-dd"), valores = d.Values });
                return Results.Json(new { fuso = "America/Sao_Paulo", dias = days });
            });

            // Baixa uma cópia: ?nome=AAAA-MM-DD (guardada) ou sem nome (as contas como estão agora).
            app.MapGet(Path + "/backup", async (HttpContext ctx, BackupService backups) =>
            {
                var deny = Deny(ctx);
                if (deny != null) return deny;
                string name = ctx.Request.Query["nome"];
                byte[] data;
                if (string.IsNullOrEmpty(name))
                {
                    data = (await backups.CurrentAsync()).Data;
                    name = StatsBook.DayOf(DateTime.UtcNow).ToString("yyyy-MM-dd") + "_agora";
                }
                else
                {
                    data = await backups.Store.GetAsync(name, ctx.RequestAborted);
                    if (data == null) return Results.NotFound();
                }
                string file = "fodinha-contas-" + SafeFileName(name) + ".json.gz";
                return Results.File(data, "application/gzip", file);
            });

            // Restaura: de uma cópia guardada (campo "nome") ou de um arquivo baixado antes (campo "arquivo").
            app.MapPost(Path + "/restaurar", async (HttpContext ctx, BackupService backups) =>
            {
                var deny = Deny(ctx);
                if (deny != null) return deny;
                // Contra outro site mandando o formulário pelo navegador do dono (a senha vai junto sozinha).
                if (!SameOrigin(ctx.Request)) return Results.StatusCode(403);
                var size = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (size != null && !size.IsReadOnly) size.MaxRequestBodySize = MaxUploadBytes;
                if (!ctx.Request.HasFormContentType) return Back("Pedido inválido.");

                var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
                if (form["confirmar"].ToString().Trim().ToUpperInvariant() != "RESTAURAR")
                    return Back("Digite RESTAURAR para confirmar. Nada foi alterado.");

                byte[] data;
                var file = form.Files.GetFile("arquivo");
                if (file != null && file.Length > 0)
                {
                    using var ms = new MemoryStream();
                    await file.CopyToAsync(ms, ctx.RequestAborted);
                    data = ms.ToArray();
                }
                else
                {
                    data = await backups.Store.GetAsync(form["nome"], ctx.RequestAborted);
                    if (data == null) return Back("Cópia não encontrada. Nada foi alterado.");
                }

                try
                {
                    var result = await backups.RestoreAsync(data, ctx.RequestAborted);
                    return Back("Restaurado: " + result + ". Cópia de como estava antes: " + result.SafetyBackup + ".");
                }
                catch (InvalidDataException e)
                {
                    return Back("Arquivo recusado: " + e.Message + " Nada foi alterado.");
                }
            });
        }

        private static IResult Back(string message) => Results.Redirect(Path + "?msg=" + Uri.EscapeDataString(message));

        public static bool SameOrigin(HttpRequest r)
        {
            string origin = r.Headers.Origin;
            if (string.IsNullOrEmpty(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
            return string.Equals(uri.Authority, r.Host.Value, StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeFileName(string name) =>
            new string((name ?? "").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').Take(60).ToArray());

        // ------------------------------------------------------------------ página

        // O servidor roda com InvariantGlobalization (sem culturas): números e dias no jeito brasileiro feitos à mão.
        private static readonly NumberFormatInfo Br = new NumberFormatInfo { NumberGroupSeparator = ".", NumberDecimalSeparator = ",", NumberGroupSizes = new[] { 3 } };
        private static readonly string[] WeekDays = { "dom", "seg", "ter", "qua", "qui", "sex", "sáb" };

        private static string Dia(DateOnly d) => WeekDays[(int)d.DayOfWeek] + " " + d.ToString("dd/MM", CultureInfo.InvariantCulture);

        private static string H(string s) => WebUtility.HtmlEncode(s ?? "");

        private static string N(long v) => v.ToString("#,0", Br);

        private static long SiteSum(DayStats d, string metric) =>
            d[metric + "_site-iphone"] + d[metric + "_site-android"] + d[metric + "_site-pc"];

        internal static string Page(StatsBook stats, Now now, List<BackupInfo> backups, string backupError, string message)
        {
            var days = stats.Recent(30);
            var today = days[0];
            var yesterday = days[1];
            var sb = new StringBuilder(32 * 1024);
            string clock = DateTime.UtcNow.AddHours(StatsBook.UtcOffsetHours).ToString("dd/MM 'às' HH:mm", CultureInfo.InvariantCulture);

            sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\">")
              .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
              .Append("<meta name=\"robots\" content=\"noindex, nofollow\"><title>Painel do Fodinha</title><style>")
              .Append(Css).Append("</style></head><body><main>");

            sb.Append("<header><div><h1>Painel do Fodinha</h1><p class=\"sub\">Atualizado em ").Append(H(clock))
              .Append(" (horário de Brasília)</p></div><a class=\"btn\" href=\"").Append(Path).Append("\">Atualizar</a></header>");

            if (!string.IsNullOrEmpty(message))
                sb.Append("<p class=\"note\" role=\"status\">").Append(H(message)).Append("</p>");

            // ---- agora
            sb.Append("<section><h2>Agora</h2><div class=\"tiles\">");
            Tile(sb, "Conectados", now.Connected, null);
            Tile(sb, "Partidas em andamento", now.Matches, null);
            Tile(sb, "Jogando agora", now.Humans, null);
            Tile(sb, "Contas no total", now.Accounts, null);
            sb.Append("</div></section>");

            // ---- hoje
            sb.Append("<section><h2>Hoje</h2><p class=\"sub\">Comparado com ontem no mesmo dia inteiro.</p><div class=\"tiles\">");
            Tile(sb, "Visitantes do site", today[Metric.Visitors], yesterday[Metric.Visitors]);
            Tile(sb, "Vieram por convite", today[Metric.InviteVisitors], yesterday[Metric.InviteVisitors]);
            Tile(sb, "Jogadores", today[Metric.Players], yesterday[Metric.Players]);
            Tile(sb, "Contas novas", today[Metric.NewAccounts], yesterday[Metric.NewAccounts]);
            Tile(sb, "Partidas online", today[Metric.Matches], yesterday[Metric.Matches]);
            Tile(sb, "Vídeos assistidos", today[Metric.Rewards], yesterday[Metric.Rewards]);
            Tile(sb, "Mensagens prontas", today[Metric.Chats], yesterday[Metric.Chats]);
            Tile(sb, "Pico de conectados", today[Metric.PeakOnline], yesterday[Metric.PeakOnline]);
            sb.Append("</div></section>");

            // ---- de onde vêm
            sb.Append("<section><h2>De onde vêm (hoje)</h2><div class=\"scroll\"><table><thead><tr><th>Onde</th>")
              .Append("<th class=\"n\">Jogadores</th><th class=\"n\">Contas novas</th><th class=\"n\">Visitantes do site</th></tr></thead><tbody>");
            Row(sb, "App Android", today[Metric.Players + "_android"], today[Metric.NewAccounts + "_android"], null);
            Row(sb, "Site no iPhone", today[Metric.Players + "_site-iphone"], today[Metric.NewAccounts + "_site-iphone"], today[Metric.Visitors + "_iphone"]);
            Row(sb, "Site no Android", today[Metric.Players + "_site-android"], today[Metric.NewAccounts + "_site-android"], today[Metric.Visitors + "_android"]);
            Row(sb, "Site no computador", today[Metric.Players + "_site-pc"], today[Metric.NewAccounts + "_site-pc"], today[Metric.Visitors + "_pc"]);
            long other = today[Metric.Players + "_outro"] + today[Metric.Players + "_pc"] + today[Metric.Players + "_ios"];
            long otherNew = today[Metric.NewAccounts + "_outro"] + today[Metric.NewAccounts + "_pc"] + today[Metric.NewAccounts + "_ios"];
            Row(sb, "Outros (app antigo, PC)", other, otherNew, null);
            sb.Append("</tbody></table></div><p class=\"sub\">Instalações oficiais: Play Console. Aqui, \"contas novas no app\" são as instalações que abriram o jogo.</p></section>");

            // ---- 30 dias
            sb.Append("<section><h2>Últimos 30 dias</h2><div class=\"charts\">");
            Chart(sb, "Visitantes do site", days, d => d[Metric.Visitors]);
            Chart(sb, "Jogadores", days, d => d[Metric.Players]);
            Chart(sb, "Contas novas", days, d => d[Metric.NewAccounts]);
            Chart(sb, "Partidas online", days, d => d[Metric.Matches]);
            sb.Append("</div></section>");

            // ---- dia a dia (a tabela é a versão acessível dos gráficos)
            sb.Append("<section><h2>Dia a dia</h2><div class=\"scroll\"><table><thead><tr><th>Dia</th>")
              .Append("<th class=\"n\">Visitantes</th><th class=\"n\">Convite</th><th class=\"n\">Jogadores</th><th class=\"n\">App</th>")
              .Append("<th class=\"n\">Site</th><th class=\"n\">Contas novas</th><th class=\"n\">Partidas</th><th class=\"n\">Vídeos</th>")
              .Append("<th class=\"n\">Mensagens</th><th class=\"n\">Pico</th></tr></thead><tbody>");
            foreach (var d in days)
            {
                sb.Append("<tr><td>").Append(Dia(d.Day)).Append("</td>");
                foreach (long v in new[]
                         {
                             d[Metric.Visitors], d[Metric.InviteVisitors], d[Metric.Players], d[Metric.Players + "_android"],
                             SiteSum(d, Metric.Players), d[Metric.NewAccounts], d[Metric.Matches], d[Metric.Rewards], d[Metric.Chats],
                             d[Metric.PeakOnline]
                         })
                    sb.Append("<td class=\"n\">").Append(N(v)).Append("</td>");
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table></div></section>");

            // ---- cópias de segurança
            sb.Append("<section><h2>Cópias de segurança</h2>");
            if (!string.IsNullOrEmpty(backupError))
                sb.Append("<p class=\"status bad\"><span aria-hidden=\"true\">●</span> Problema na última cópia: ").Append(H(backupError)).Append("</p>");
            else if (backups.Count > 0)
                sb.Append("<p class=\"status good\"><span aria-hidden=\"true\">●</span> Cópia automática todo dia; ficam guardadas ")
                  .Append(BackupService.KeepDays).Append(" dias.</p>");
            sb.Append("<p><a class=\"btn\" href=\"").Append(Path).Append("/backup\">Baixar cópia de agora</a></p>")
              .Append("<p class=\"sub\">Guarde uma cópia no seu computador ou no Google Drive de vez em quando: se o banco inteiro sumir, é ela que traz as contas de volta.</p>");
            if (backups.Count == 0) sb.Append("<p class=\"sub\">Nenhuma cópia guardada ainda (a primeira sai 1 minuto depois do servidor ligar).</p>");
            else
            {
                sb.Append("<div class=\"scroll\"><table><thead><tr><th>Cópia</th><th class=\"n\">Contas</th><th class=\"n\">Tamanho</th><th></th></tr></thead><tbody>");
                foreach (var b in backups)
                {
                    sb.Append("<tr><td>").Append(H(b.Name)).Append("<br><span class=\"sub\">")
                      .Append(b.CreatedAt.AddHours(StatsBook.UtcOffsetHours).ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)).Append("</span></td>")
                      .Append("<td class=\"n\">").Append(N(b.Accounts)).Append("</td><td class=\"n\">").Append(Size(b.Bytes)).Append("</td><td class=\"actions\">")
                      .Append("<a href=\"").Append(Path).Append("/backup?nome=").Append(Uri.EscapeDataString(b.Name)).Append("\">Baixar</a>")
                      .Append("<details><summary>Restaurar</summary>");
                    RestoreForm(sb, "<input type=\"hidden\" name=\"nome\" value=\"" + H(b.Name) + "\">");
                    sb.Append("</details></td></tr>");
                }
                sb.Append("</tbody></table></div>");
            }
            sb.Append("<details class=\"upload\"><summary>Restaurar de um arquivo baixado antes</summary>");
            RestoreForm(sb, "<input type=\"file\" name=\"arquivo\" accept=\".gz,application/gzip\" required>");
            sb.Append("</details><p class=\"sub\">Restaurar volta moedas, itens e avatares das contas da cópia. Contas criadas depois dela continuam como estão. ")
              .Append("Antes de restaurar, o servidor guarda uma cópia de como estava (para desfazer).</p></section>");

            sb.Append("</main><div id=\"tip\" role=\"tooltip\" hidden></div><script>").Append(Script).Append("</script></body></html>");
            return sb.ToString();
        }

        private static void RestoreForm(StringBuilder sb, string field)
        {
            sb.Append("<form method=\"post\" action=\"").Append(Path).Append("/restaurar\" enctype=\"multipart/form-data\">")
              .Append(field)
              .Append("<label>Para confirmar, digite RESTAURAR<input name=\"confirmar\" autocomplete=\"off\" required></label>")
              .Append("<button class=\"btn danger\" type=\"submit\">Restaurar contas</button></form>");
        }

        private static string Size(long bytes) =>
            bytes >= 1024 * 1024 ? (bytes / (1024.0 * 1024.0)).ToString("0.0", Br) + " MB" : Math.Max(1, bytes / 1024) + " KB";

        private static void Tile(StringBuilder sb, string label, long value, long? previous)
        {
            sb.Append("<div class=\"tile\"><div class=\"label\">").Append(H(label)).Append("</div><div class=\"value\">").Append(N(value)).Append("</div>");
            if (previous is long p)
            {
                long diff = value - p;
                string cls = diff > 0 ? "up" : diff < 0 ? "down" : "same";
                string arrow = diff > 0 ? "▲" : diff < 0 ? "▼" : "=";
                sb.Append("<div class=\"delta ").Append(cls).Append("\"><span aria-hidden=\"true\">").Append(arrow).Append("</span> ")
                  .Append(diff > 0 ? "+" : "").Append(N(diff)).Append(" <span class=\"vs\">vs ontem (").Append(N(p)).Append(")</span></div>");
            }
            sb.Append("</div>");
        }

        private static void Row(StringBuilder sb, string label, long players, long created, long? visitors)
        {
            sb.Append("<tr><td>").Append(H(label)).Append("</td><td class=\"n\">").Append(N(players)).Append("</td><td class=\"n\">")
              .Append(N(created)).Append("</td><td class=\"n\">").Append(visitors is long v ? N(v) : "—").Append("</td></tr>");
        }

        /// <summary>Colunas de um número nos últimos 30 dias (mais antigo à esquerda). Toque/passe o mouse para ver o valor.</summary>
        private static void Chart(StringBuilder sb, string title, List<DayStats> newestFirst, Func<DayStats, long> value)
        {
            const float W = 420f, Hh = 180f, Top = 22f, Bottom = 26f, Left = 40f;
            var days = Enumerable.Reverse(newestFirst).ToList();
            long max = Math.Max(1, days.Max(value));
            long tickMax = NiceCeiling(max);
            float plotH = Hh - Top - Bottom;
            float slot = (W - Left) / days.Count;
            float bar = Math.Min(24f, slot * 0.62f);

            sb.Append("<figure class=\"chart\"><figcaption>").Append(H(title))
              .Append(" <span class=\"sub\">hoje: ").Append(N(value(newestFirst[0]))).Append("</span></figcaption>")
              .Append("<svg viewBox=\"0 0 ").Append(W).Append(' ').Append(Hh).Append("\" role=\"img\" aria-label=\"").Append(H(title)).Append(" nos últimos 30 dias\">");

            // grade (0, metade, topo) e rótulos do eixo
            foreach (var t in new[] { 0L, tickMax / 2, tickMax })
            {
                float y = Top + plotH - plotH * t / tickMax;
                sb.Append("<line class=\"").Append(t == 0 ? "axis" : "grid").Append("\" x1=\"").Append(F(Left)).Append("\" x2=\"").Append(F(W))
                  .Append("\" y1=\"").Append(F(y)).Append("\" y2=\"").Append(F(y)).Append("\"/>")
                  .Append("<text class=\"tick\" x=\"").Append(F(Left - 6)).Append("\" y=\"").Append(F(y + 4)).Append("\" text-anchor=\"end\">")
                  .Append(Compact(t)).Append("</text>");
            }

            for (int i = 0; i < days.Count; i++)
            {
                long v = value(days[i]);
                float x = Left + slot * i + (slot - bar) / 2f;
                float h = plotH * v / tickMax;
                float y = Top + plotH - h;
                string tip = Dia(days[i].Day) + ": " + N(v);
                if (v > 0)
                {
                    float r = Math.Min(4f, Math.Min(h, bar / 2f));
                    // ponta arredondada, base reta
                    sb.Append("<path class=\"bar\" d=\"M").Append(F(x)).Append(',').Append(F(Top + plotH))
                      .Append("V").Append(F(y + r)).Append("Q").Append(F(x)).Append(',').Append(F(y)).Append(' ').Append(F(x + r)).Append(',').Append(F(y))
                      .Append("H").Append(F(x + bar - r)).Append("Q").Append(F(x + bar)).Append(',').Append(F(y)).Append(' ').Append(F(x + bar)).Append(',').Append(F(y + r))
                      .Append("V").Append(F(Top + plotH)).Append("Z\"/>");
                }
                // área de toque maior que a coluna
                sb.Append("<rect class=\"hit\" x=\"").Append(F(Left + slot * i)).Append("\" y=\"").Append(F(Top)).Append("\" width=\"").Append(F(slot))
                  .Append("\" height=\"").Append(F(plotH)).Append("\" data-tip=\"").Append(H(tip)).Append("\"><title>").Append(H(tip)).Append("</title></rect>");
            }

            // rótulo só do maior valor (os outros ficam no toque e na tabela)
            int best = days.FindLastIndex(d => value(d) == max);
            if (best >= 0 && value(days[best]) > 0)
            {
                float bx = Left + slot * best + slot / 2f;
                float by = Top + plotH - plotH * value(days[best]) / tickMax - 6f;
                // Perto da borda direita o número encosta no fim da coluna (não sai cortado).
                bool atEnd = bx > W - 24f;
                if (atEnd) bx = Left + slot * best + (slot + bar) / 2f;
                sb.Append("<text class=\"val\" x=\"").Append(F(bx)).Append("\" y=\"").Append(F(Math.Max(13f, by))).Append("\" text-anchor=\"").Append(atEnd ? "end" : "middle").Append("\">")
                  .Append(N(value(days[best]))).Append("</text>");
            }

            // datas: primeira, do meio e hoje
            foreach (int i in new[] { 0, days.Count / 2, days.Count - 1 })
            {
                float x = Left + slot * i + slot / 2f;
                string anchor = i == 0 ? "start" : i == days.Count - 1 ? "end" : "middle";
                if (i == 0) x = Left + (slot - bar) / 2f;
                if (i == days.Count - 1) x = Left + slot * i + (slot + bar) / 2f;
                sb.Append("<text class=\"tick\" x=\"").Append(F(x)).Append("\" y=\"").Append(F(Hh - 6)).Append("\" text-anchor=\"").Append(anchor).Append("\">")
                  .Append(i == days.Count - 1 ? "hoje" : days[i].Day.ToString("dd/MM", CultureInfo.InvariantCulture)).Append("</text>");
            }
            sb.Append("</svg></figure>");
        }

        private static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        private static long NiceCeiling(long max)
        {
            if (max <= 4) return 4;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(max)));
            foreach (double m in new[] { 1, 2, 2.5, 4, 5, 10 })
                if (m * mag >= max && ((long)(m * mag)) % 2 == 0) return (long)(m * mag);
            return (long)(10 * mag);
        }

        private static string Compact(long v) =>
            v >= 1_000_000 ? (v / 1_000_000.0).ToString("0.#", Br) + " mi"
            : v >= 10_000 ? (v / 1000.0).ToString("0.#", Br) + " mil"
            : N(v);

        private const string Css = @"
:root{color-scheme:light;--page:#f9f9f7;--surface:#fcfcfb;--ink:#0b0b0b;--ink2:#52514e;--muted:#898781;--grid:#e1e0d9;--axis:#c3c2b7;--border:rgba(11,11,11,.10);--series:#2a78d6;--up:#006300;--down:#d03b3b;--btn:#0b0b0b;--btnink:#fcfcfb}
@media (prefers-color-scheme:dark){:root{color-scheme:dark;--page:#0d0d0d;--surface:#1a1a19;--ink:#f0efec;--ink2:#c3c2b7;--muted:#898781;--grid:#2c2c2a;--axis:#383835;--border:rgba(255,255,255,.10);--series:#3987e5;--up:#0ca30c;--down:#e66767;--btn:#f0efec;--btnink:#1a1a19}}
*{box-sizing:border-box}
body{margin:0;background:var(--page);color:var(--ink);font:15px/1.45 system-ui,-apple-system,'Segoe UI',sans-serif}
main{max-width:1080px;margin:0 auto;padding:20px 16px 48px}
header{display:flex;justify-content:space-between;align-items:center;gap:12px;margin-bottom:8px}
h1{font-size:22px;margin:0}
h2{font-size:16px;margin:28px 0 10px}
.sub{color:var(--ink2);font-size:13px;margin:2px 0 8px}
.note{background:var(--surface);border:1px solid var(--border);border-radius:10px;padding:10px 12px}
.tiles{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:10px}
.tile{background:var(--surface);border:1px solid var(--border);border-radius:12px;padding:12px}
.label{color:var(--ink2);font-size:13px}
.value{font-size:28px;font-weight:600;font-variant-numeric:tabular-nums;margin-top:2px}
.delta{font-size:12px;margin-top:2px;font-variant-numeric:tabular-nums}
.delta.up{color:var(--up)}.delta.down{color:var(--down)}.delta.same{color:var(--muted)}
.vs{color:var(--muted)}
.charts{display:grid;grid-template-columns:repeat(auto-fill,minmax(300px,1fr));gap:10px}
.chart{margin:0;background:var(--surface);border:1px solid var(--border);border-radius:12px;padding:12px}
figcaption{font-weight:600;margin-bottom:6px}
svg{width:100%;height:auto;display:block}
.bar{fill:var(--series)}
.grid{stroke:var(--grid);stroke-width:1}
.axis{stroke:var(--axis);stroke-width:1}
.tick{fill:var(--muted);font-size:13px}
.val{fill:var(--ink2);font-size:13px;font-weight:600}
.hit{fill:transparent;cursor:pointer}
.hit:hover{fill:var(--grid);opacity:.35}
.scroll{overflow-x:auto;background:var(--surface);border:1px solid var(--border);border-radius:12px}
table{border-collapse:collapse;width:100%;font-variant-numeric:tabular-nums}
th,td{padding:8px 10px;border-bottom:1px solid var(--grid);text-align:left;white-space:nowrap;vertical-align:top}
th{color:var(--ink2);font-weight:600;font-size:13px}
tr:last-child td{border-bottom:0}
.n{text-align:right}
.actions a{margin-right:12px}
a{color:var(--series)}
.btn{display:inline-block;background:var(--btn);color:var(--btnink);border:0;border-radius:8px;padding:8px 14px;font:inherit;font-weight:600;text-decoration:none;cursor:pointer}
.btn.danger{background:var(--down);color:#fff}
.status{font-weight:600}.status.good{color:var(--up)}.status.bad{color:var(--down)}
details{margin-top:6px}summary{cursor:pointer;color:var(--series)}
form{display:grid;gap:8px;margin:8px 0;max-width:340px;white-space:normal}
label{display:grid;gap:4px;font-size:13px;color:var(--ink2)}
input{font:inherit;padding:7px 9px;border:1px solid var(--axis);border-radius:8px;background:var(--page);color:var(--ink)}
.upload{background:var(--surface);border:1px solid var(--border);border-radius:12px;padding:10px 12px;margin-top:10px}
#tip{position:fixed;z-index:9;pointer-events:none;background:var(--ink);color:var(--surface);font-size:12px;font-weight:600;padding:5px 8px;border-radius:6px;white-space:nowrap}
";

        // Mostra o valor da coluna ao tocar (celular) ou passar o mouse.
        private const string Script = @"
(function(){var tip=document.getElementById('tip');
function show(e){var t=e.target;if(!t.dataset||!t.dataset.tip)return;tip.textContent=t.dataset.tip;tip.hidden=false;
var x=e.clientX||0,y=e.clientY||0;tip.style.left=Math.min(window.innerWidth-tip.offsetWidth-8,Math.max(8,x-tip.offsetWidth/2))+'px';tip.style.top=Math.max(8,y-40)+'px';}
document.addEventListener('pointermove',show);document.addEventListener('pointerdown',show);
document.addEventListener('pointerout',function(e){if(e.target.dataset&&e.target.dataset.tip)tip.hidden=true;});
document.querySelectorAll('svg title').forEach(function(t){t.remove();});})();
";
    }
}
