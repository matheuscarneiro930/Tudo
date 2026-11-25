using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Fodinha.Server
{
    /// <summary>Nomes dos números do painel (gravados no banco: não renomear).</summary>
    public static class Metric
    {
        public const string Visits = "visitas";                 // + "_iphone" / "_android" / "_pc"
        public const string Visitors = "visitantes";            // pessoas diferentes no dia (+ aparelho)
        public const string InviteVisits = "visitas_convite";   // chegaram pelo botão "Convidar amigos"
        public const string InviteVisitors = "visitantes_convite";
        public const string NewAccounts = "contas_novas";       // + "_" + plataforma
        public const string Players = "jogadores";              // contas diferentes que entraram no dia (+ plataforma)
        public const string PeakOnline = "pico_online";
        public const string Matches = "partidas";
        public const string MatchesFinished = "partidas_terminadas";
        public const string MatchHumans = "humanos_em_partidas";
        public const string StakeMatches = "partidas_valendo";
        public const string CoinsInPots = "moedas_apostadas";
        public const string Rewards = "videos";
        public const string Chats = "mensagens";
        public const string ShopPurchases = "compras_loja";
        public const string Vip = "vip";

        /// <summary>Números que guardam o MAIOR valor do dia (não somam).</summary>
        public static bool IsPeak(string metric) => metric == PeakOnline;

        /// <summary>Plataformas que o app informa no "hello". Qualquer outra coisa vira "outro".</summary>
        public static readonly string[] Platforms = { "android", "ios", "site-iphone", "site-android", "site-pc", "pc" };

        public static string NormalizePlatform(string platform) =>
            platform != null && Array.IndexOf(Platforms, platform) >= 0 ? platform : "outro";

        /// <summary>Aparelho de quem abriu o site, pelo User-Agent do navegador.</summary>
        public static string DeviceOf(string userAgent)
        {
            string ua = userAgent ?? "";
            if (ua.Contains("iPhone") || ua.Contains("iPad") || ua.Contains("iPod")) return "iphone";
            if (ua.Contains("Android")) return "android";
            return "pc";
        }

        /// <summary>Robôs de busca, pré-visualização de link (WhatsApp etc.), monitores: não contam como visita.</summary>
        public static bool IsRobot(string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent)) return true;
            string ua = userAgent.ToLowerInvariant();
            foreach (var word in RobotWords) if (ua.Contains(word)) return true;
            return !ua.Contains("mozilla"); // todo navegador de verdade se apresenta como "Mozilla/5.0 ..."
        }

        private static readonly string[] RobotWords =
        {
            "bot", "crawl", "spider", "slurp", "preview", "facebookexternalhit", "whatsapp", "telegram", "discord",
            "monitor", "uptime", "pingdom", "render", "curl", "wget", "python", "go-http", "java/", "okhttp", "headless",
            "lighthouse", "scan"
        };
    }

    /// <summary>Uma "pessoa diferente no dia" já vista (só o código embaralhado, nunca o IP).</summary>
    public readonly struct UniqueKey
    {
        public readonly DateOnly Day;
        public readonly string Kind;
        public readonly string Key;

        public UniqueKey(DateOnly day, string kind, string key)
        {
            Day = day;
            Kind = kind;
            Key = key;
        }
    }

    /// <summary>O que estava guardado no banco ao ligar o servidor.</summary>
    public sealed class StatsState
    {
        public string Salt;
        public List<(DateOnly Day, string Metric, long Value)> Values = new List<(DateOnly, string, long)>();
        public List<UniqueKey> Uniques = new List<UniqueKey>();
    }

    /// <summary>Mudanças desde a última gravação (somas, picos, pessoas novas, limpeza).</summary>
    public sealed class StatsChanges
    {
        public Dictionary<(DateOnly Day, string Metric), long> Adds = new Dictionary<(DateOnly, string), long>();
        public Dictionary<(DateOnly Day, string Metric), long> Peaks = new Dictionary<(DateOnly, string), long>();
        public List<UniqueKey> Uniques = new List<UniqueKey>();
        /// <summary>Apagar as "pessoas do dia" de dias anteriores a este (não precisam mais).</summary>
        public DateOnly? PurgeUniquesBefore;
        /// <summary>Segredo novo para embaralhar os IPs (só na 1ª vez).</summary>
        public string NewSalt;

        public bool IsEmpty => Adds.Count == 0 && Peaks.Count == 0 && Uniques.Count == 0 && PurgeUniquesBefore == null && NewSalt == null;
    }

    /// <summary>Números de um dia.</summary>
    public sealed class DayStats
    {
        public DateOnly Day;
        public Dictionary<string, long> Values = new Dictionary<string, long>();

        public long this[string metric] => Values.TryGetValue(metric, out var v) ? v : 0;
    }

    /// <summary>
    /// Números do jogo por dia (horário de Brasília), para o painel do dono. Seguro para várias threads
    /// (o site e o laço do jogo contam ao mesmo tempo). Não guarda IP: "pessoas diferentes" são contadas por um
    /// código embaralhado com um segredo do servidor, que é apagado no dia seguinte.
    /// </summary>
    public sealed class StatsBook
    {
        public const int UtcOffsetHours = -3; // Brasília (sem horário de verão desde 2019)
        public const int DaysKept = 400;

        private readonly object gate = new object();
        private readonly Func<DateTime> clock;
        private readonly Dictionary<DateOnly, DayStats> days = new Dictionary<DateOnly, DayStats>();
        private readonly HashSet<string> seenToday = new HashSet<string>();
        private DateOnly seenDay;
        private StatsChanges pending = new StatsChanges();
        private byte[] salt;

        public StatsBook(Func<DateTime> utcClock = null)
        {
            clock = utcClock ?? (() => DateTime.UtcNow);
            seenDay = Today;
            salt = RandomNumberGenerator.GetBytes(32);
        }

        public static DateOnly DayOf(DateTime utc) => DateOnly.FromDateTime(utc.AddHours(UtcOffsetHours));

        public DateOnly Today => DayOf(clock());

        /// <summary>Carrega o que estava no banco. Sem segredo salvo, cria um (e grava na próxima vez).</summary>
        public void Load(StatsState state)
        {
            lock (gate)
            {
                if (state == null) return;
                if (!string.IsNullOrEmpty(state.Salt)) salt = Convert.FromBase64String(state.Salt);
                else pending.NewSalt = Convert.ToBase64String(salt);
                foreach (var (day, metric, value) in state.Values)
                {
                    var d = DayLocked(day);
                    d.Values.TryGetValue(metric, out var cur);
                    d.Values[metric] = Metric.IsPeak(metric) ? Math.Max(cur, value) : cur + value;
                }
                var today = Today;
                foreach (var u in state.Uniques)
                    if (u.Day == today) seenToday.Add(u.Kind + "|" + u.Key);
                seenDay = today;
            }
        }

        /// <summary>Soma <paramref name="amount"/> no número de hoje.</summary>
        public void Add(string metric, long amount = 1)
        {
            if (string.IsNullOrEmpty(metric) || amount == 0) return;
            lock (gate)
            {
                var today = Today;
                var d = DayLocked(today);
                d.Values.TryGetValue(metric, out var cur);
                d.Values[metric] = cur + amount;
                pending.Adds.TryGetValue((today, metric), out var p);
                pending.Adds[(today, metric)] = p + amount;
            }
        }

        /// <summary>Guarda o maior valor do dia (ex.: pico de gente conectada).</summary>
        public void Peak(string metric, long value)
        {
            lock (gate)
            {
                var today = Today;
                var d = DayLocked(today);
                if (d.Values.TryGetValue(metric, out var cur) && cur >= value) return;
                d.Values[metric] = value;
                pending.Peaks[(today, metric)] = value;
            }
        }

        /// <summary>Primeira vez HOJE que este (tipo, chave) aparece? Ex.: ("jogador", id da conta).</summary>
        public bool FirstToday(string kind, string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            lock (gate)
            {
                var today = Today;
                if (today != seenDay)
                {
                    seenToday.Clear();
                    seenDay = today;
                    pending.PurgeUniquesBefore = today;
                }
                if (!seenToday.Add(kind + "|" + key)) return false;
                pending.Uniques.Add(new UniqueKey(today, kind, key));
                return true;
            }
        }

        /// <summary>Código embaralhado (HMAC) que não deixa descobrir o texto original (ex.: o IP).</summary>
        public string Hash(string text)
        {
            byte[] key;
            lock (gate) key = salt;
            using var h = new HMACSHA256(key);
            var bytes = h.ComputeHash(Encoding.UTF8.GetBytes(Today.ToString("yyyy-MM-dd") + "|" + (text ?? "")));
            return Convert.ToHexString(bytes, 0, 12).ToLowerInvariant();
        }

        /// <summary>Pega as mudanças para gravar (e zera a lista).</summary>
        public StatsChanges TakeChanges()
        {
            lock (gate)
            {
                var c = pending;
                pending = new StatsChanges();
                return c;
            }
        }

        /// <summary>A gravação falhou: devolve as mudanças para tentar de novo depois.</summary>
        public void PutBack(StatsChanges failed)
        {
            if (failed == null) return;
            lock (gate)
            {
                foreach (var kv in failed.Adds)
                {
                    pending.Adds.TryGetValue(kv.Key, out var p);
                    pending.Adds[kv.Key] = p + kv.Value;
                }
                foreach (var kv in failed.Peaks)
                    if (!pending.Peaks.TryGetValue(kv.Key, out var p) || p < kv.Value) pending.Peaks[kv.Key] = kv.Value;
                pending.Uniques.InsertRange(0, failed.Uniques);
                if (failed.PurgeUniquesBefore != null && (pending.PurgeUniquesBefore == null || pending.PurgeUniquesBefore < failed.PurgeUniquesBefore))
                    pending.PurgeUniquesBefore = failed.PurgeUniquesBefore;
                pending.NewSalt ??= failed.NewSalt;
            }
        }

        /// <summary>Os últimos <paramref name="count"/> dias, do mais novo para o mais antigo (dias sem nada vêm zerados).</summary>
        public List<DayStats> Recent(int count)
        {
            lock (gate)
            {
                var list = new List<DayStats>();
                var day = Today;
                for (int i = 0; i < count; i++, day = day.AddDays(-1))
                {
                    var copy = new DayStats { Day = day };
                    if (days.TryGetValue(day, out var d)) copy.Values = new Dictionary<string, long>(d.Values);
                    list.Add(copy);
                }
                return list;
            }
        }

        /// <summary>Soma de um número em todos os dias guardados.</summary>
        public long Total(string metric)
        {
            lock (gate) return days.Values.Sum(d => d[metric]);
        }

        private DayStats DayLocked(DateOnly day)
        {
            if (!days.TryGetValue(day, out var d))
            {
                days[day] = d = new DayStats { Day = day };
                if (days.Count > DaysKept)
                {
                    var oldest = days.Keys.Min();
                    days.Remove(oldest);
                }
            }
            return d;
        }
    }

    /// <summary>Onde os números ficam guardados.</summary>
    public interface IStatsStore
    {
        Task<StatsState> LoadAsync(DateOnly today, CancellationToken ct = default);
        Task SaveAsync(StatsChanges changes, CancellationToken ct = default);
    }

    /// <summary>Sem banco (testes / desenvolvimento).</summary>
    public sealed class MemoryStatsStore : IStatsStore
    {
        private readonly object gate = new object();
        private readonly Dictionary<(DateOnly, string), long> values = new Dictionary<(DateOnly, string), long>();
        private readonly HashSet<(DateOnly, string, string)> uniques = new HashSet<(DateOnly, string, string)>();
        private string salt;
        public int Saves;

        public Task<StatsState> LoadAsync(DateOnly today, CancellationToken ct = default)
        {
            lock (gate)
            {
                var s = new StatsState { Salt = salt };
                foreach (var kv in values) s.Values.Add((kv.Key.Item1, kv.Key.Item2, kv.Value));
                foreach (var u in uniques) if (u.Item1 == today) s.Uniques.Add(new UniqueKey(u.Item1, u.Item2, u.Item3));
                return Task.FromResult(s);
            }
        }

        public Task SaveAsync(StatsChanges c, CancellationToken ct = default)
        {
            lock (gate)
            {
                Saves++;
                foreach (var kv in c.Adds)
                {
                    values.TryGetValue(kv.Key, out var cur);
                    values[kv.Key] = cur + kv.Value;
                }
                foreach (var kv in c.Peaks)
                    if (!values.TryGetValue(kv.Key, out var cur) || cur < kv.Value) values[kv.Key] = kv.Value;
                foreach (var u in c.Uniques) uniques.Add((u.Day, u.Kind, u.Key));
                if (c.PurgeUniquesBefore is DateOnly before) uniques.RemoveWhere(u => u.Item1 < before);
                salt ??= c.NewSalt;
                return Task.CompletedTask;
            }
        }

        public long Value(DateOnly day, string metric)
        {
            lock (gate) return values.TryGetValue((day, metric), out var v) ? v : 0;
        }

        public int UniqueCount
        {
            get { lock (gate) return uniques.Count; }
        }
    }

    /// <summary>PostgreSQL (o mesmo banco das contas).</summary>
    public sealed class PostgresStatsStore : IStatsStore
    {
        private readonly string connectionString;

        public PostgresStatsStore(string databaseUrl)
        {
            connectionString = PostgresAccountStore.ToConnectionString(databaseUrl);
        }

        private static async Task EnsureTablesAsync(NpgsqlConnection conn, CancellationToken ct)
        {
            const string sql = @"
create table if not exists stats_daily (
  day date not null, metric text not null, value bigint not null default 0,
  primary key (day, metric));
create table if not exists stats_unique (
  day date not null, kind text not null, key text not null,
  primary key (day, kind, key));
create table if not exists stats_meta (key text primary key, value text not null);";
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<StatsState> LoadAsync(DateOnly today, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);
            await EnsureTablesAsync(conn, ct);
            var state = new StatsState();

            await using (var cmd = new NpgsqlCommand("select value from stats_meta where key = 'salt'", conn))
                state.Salt = (string)await cmd.ExecuteScalarAsync(ct);

            await using (var cmd = new NpgsqlCommand("select day, metric, value from stats_daily where day >= @from", conn))
            {
                cmd.Parameters.AddWithValue("from", today.AddDays(-StatsBook.DaysKept));
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    state.Values.Add((r.GetFieldValue<DateOnly>(0), r.GetString(1), r.GetInt64(2)));
            }

            await using (var cmd = new NpgsqlCommand("select kind, key from stats_unique where day = @today", conn))
            {
                cmd.Parameters.AddWithValue("today", today);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) state.Uniques.Add(new UniqueKey(today, r.GetString(0), r.GetString(1)));
            }
            return state;
        }

        public async Task SaveAsync(StatsChanges c, CancellationToken ct = default)
        {
            if (c.IsEmpty) return;
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            await using var batch = new NpgsqlBatch(conn, tx);

            foreach (var kv in c.Adds)
            {
                var b = new NpgsqlBatchCommand(
                    "insert into stats_daily (day, metric, value) values ($1, $2, $3) " +
                    "on conflict (day, metric) do update set value = stats_daily.value + excluded.value");
                b.Parameters.Add(new NpgsqlParameter { Value = kv.Key.Day });
                b.Parameters.Add(new NpgsqlParameter { Value = kv.Key.Metric });
                b.Parameters.Add(new NpgsqlParameter { Value = kv.Value });
                batch.BatchCommands.Add(b);
            }
            foreach (var kv in c.Peaks)
            {
                var b = new NpgsqlBatchCommand(
                    "insert into stats_daily (day, metric, value) values ($1, $2, $3) " +
                    "on conflict (day, metric) do update set value = greatest(stats_daily.value, excluded.value)");
                b.Parameters.Add(new NpgsqlParameter { Value = kv.Key.Day });
                b.Parameters.Add(new NpgsqlParameter { Value = kv.Key.Metric });
                b.Parameters.Add(new NpgsqlParameter { Value = kv.Value });
                batch.BatchCommands.Add(b);
            }
            foreach (var u in c.Uniques)
            {
                var b = new NpgsqlBatchCommand("insert into stats_unique (day, kind, key) values ($1, $2, $3) on conflict do nothing");
                b.Parameters.Add(new NpgsqlParameter { Value = u.Day });
                b.Parameters.Add(new NpgsqlParameter { Value = u.Kind });
                b.Parameters.Add(new NpgsqlParameter { Value = u.Key });
                batch.BatchCommands.Add(b);
            }
            if (c.PurgeUniquesBefore is DateOnly before)
            {
                var b = new NpgsqlBatchCommand("delete from stats_unique where day < $1");
                b.Parameters.Add(new NpgsqlParameter { Value = before });
                batch.BatchCommands.Add(b);
            }
            if (c.NewSalt != null)
            {
                var b = new NpgsqlBatchCommand("insert into stats_meta (key, value) values ('salt', $1) on conflict do nothing");
                b.Parameters.Add(new NpgsqlParameter { Value = c.NewSalt });
                batch.BatchCommands.Add(b);
            }

            await batch.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }
    }

    /// <summary>Grava os números no banco a cada poucos segundos (e ao desligar). O jogo nunca espera o banco.</summary>
    public sealed class StatsWriter : BackgroundService
    {
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

        private readonly StatsBook book;
        private readonly IStatsStore store;
        private readonly ILogger<StatsWriter> logger;

        public StatsWriter(StatsBook book, IStatsStore store, ILogger<StatsWriter> logger)
        {
            this.book = book;
            this.store = store;
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await Task.Delay(Interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
                await FlushAsync(stoppingToken);
            }
            await FlushAsync(CancellationToken.None);
        }

        /// <summary>Grava agora o que estiver pendente (também usado pelos testes).</summary>
        public async Task FlushAsync(CancellationToken ct)
        {
            var changes = book.TakeChanges();
            if (changes.IsEmpty) return;
            try
            {
                await store.SaveAsync(changes, ct);
            }
            catch (Exception e)
            {
                book.PutBack(changes);
                logger.LogWarning(e, "Falha ao gravar as estatísticas (tento de novo depois)");
            }
        }
    }
}
