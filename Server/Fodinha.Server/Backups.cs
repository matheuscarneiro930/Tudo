using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Fodinha.Server
{
    /// <summary>Uma cópia de segurança guardada.</summary>
    public sealed class BackupInfo
    {
        public string Name;
        public DateTime CreatedAt;
        public int Accounts;
        public long Bytes;
    }

    /// <summary>Resultado de uma restauração.</summary>
    public sealed class RestoreResult
    {
        public int Updated;
        public int Added;
        public int Skipped;
        public string SafetyBackup;

        public override string ToString() =>
            Updated + " contas voltaram ao estado da cópia, " + Added + " recriadas" + (Skipped > 0 ? ", " + Skipped + " ignoradas" : "");
    }

    /// <summary>
    /// Arquivo de cópia das contas: JSON compactado (gzip). Não tem senha de ninguém: o servidor só guarda o
    /// "hash" do token de cada aparelho, que não serve para entrar na conta.
    /// </summary>
    public static class BackupFile
    {
        public const string Format = "fodinha-contas";
        public const int Version = 1;
        public const int MaxAccounts = 2_000_000;
        private const long MaxUncompressedBytes = 512L * 1024 * 1024;

        private sealed class FileDto
        {
            public string format { get; set; }
            public int version { get; set; }
            public DateTime createdAt { get; set; }
            public List<AccountDto> accounts { get; set; }
        }

        private sealed class AccountDto
        {
            public string id { get; set; }
            public string tokenHash { get; set; }
            public string name { get; set; }
            public int coins { get; set; }
            public int games { get; set; }
            public int wins { get; set; }
            public DateTime createdAt { get; set; }
            public DateTime updatedAt { get; set; }
            public string avatar { get; set; }
            public List<string> owned { get; set; }
            public string table { get; set; }
            public string deck { get; set; }
            public bool noAds { get; set; }
        }

        public static byte[] Write(IReadOnlyCollection<Account> accounts, DateTime createdAtUtc)
        {
            var dto = new FileDto
            {
                format = Format,
                version = Version,
                createdAt = createdAtUtc,
                accounts = accounts.Select(a => new AccountDto
                {
                    id = a.Id,
                    tokenHash = a.TokenHash,
                    name = a.Name,
                    coins = a.Coins,
                    games = a.Games,
                    wins = a.Wins,
                    createdAt = a.CreatedAt,
                    updatedAt = a.UpdatedAt,
                    avatar = a.Avatar ?? "",
                    owned = new List<string>(a.Owned ?? new List<string>()),
                    table = a.TableTheme,
                    deck = a.DeckTheme,
                    noAds = a.NoAds
                }).ToList()
            };
            using var output = new MemoryStream();
            using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                JsonSerializer.Serialize(gz, dto);
            return output.ToArray();
        }

        /// <summary>Lê e confere uma cópia. Lança <see cref="InvalidDataException"/> se não for uma cópia válida.</summary>
        public static List<Account> Read(byte[] data)
        {
            if (data == null || data.Length < 10) throw new InvalidDataException("Arquivo vazio.");
            FileDto dto;
            try
            {
                using var input = new MemoryStream(data);
                using var gz = new GZipStream(input, CompressionMode.Decompress);
                using var limited = new LimitedStream(gz, MaxUncompressedBytes);
                dto = JsonSerializer.Deserialize<FileDto>(limited);
            }
            catch (Exception e) when (e is JsonException || e is InvalidDataException || e is IOException)
            {
                throw new InvalidDataException("Não é uma cópia do Fodinha (" + e.Message + ").");
            }
            if (dto == null || dto.format != Format) throw new InvalidDataException("Não é uma cópia do Fodinha.");
            if (dto.version != Version) throw new InvalidDataException("Versão da cópia desconhecida: " + dto.version);
            if (dto.accounts == null || dto.accounts.Count > MaxAccounts) throw new InvalidDataException("Lista de contas inválida.");

            var list = new List<Account>(dto.accounts.Count);
            var ids = new HashSet<string>();
            foreach (var a in dto.accounts)
            {
                if (a == null || string.IsNullOrEmpty(a.id) || a.id.Length > 40 || string.IsNullOrEmpty(a.tokenHash) || a.tokenHash.Length > 100)
                    throw new InvalidDataException("Conta sem identificação na cópia.");
                if (!ids.Add(a.id)) throw new InvalidDataException("Conta repetida na cópia: " + a.id);
                var owned = new List<string>();
                foreach (var item in a.owned ?? new List<string>())
                    if (Fodinha.Hosting.ShopCatalog.Find(item) != null && !owned.Contains(item)) owned.Add(item);
                list.Add(new Account
                {
                    Id = a.id,
                    TokenHash = a.tokenHash,
                    Name = NameFilter.Clean(a.name),
                    Coins = Math.Max(0, a.coins),
                    Games = Math.Max(0, a.games),
                    Wins = Math.Max(0, a.wins),
                    CreatedAt = DateTime.SpecifyKind(a.createdAt, DateTimeKind.Utc),
                    UpdatedAt = DateTime.SpecifyKind(a.updatedAt, DateTimeKind.Utc),
                    Avatar = Fodinha.Hosting.AvatarSpec.Sanitize(a.avatar),
                    Owned = owned,
                    TableTheme = Fodinha.Hosting.ShopCatalog.ValidEquipped(Fodinha.Hosting.ShopCategory.Table, a.table, owned),
                    DeckTheme = Fodinha.Hosting.ShopCatalog.ValidEquipped(Fodinha.Hosting.ShopCategory.Deck, a.deck, owned),
                    NoAds = a.noAds
                });
            }
            return list;
        }

        /// <summary>Lê no máximo N bytes (arquivo compactado "bomba" não estoura a memória).</summary>
        private sealed class LimitedStream : Stream
        {
            private readonly Stream inner;
            private long left;

            public LimitedStream(Stream inner, long max)
            {
                this.inner = inner;
                left = max;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = inner.Read(buffer, offset, (int)Math.Min(count, left + 1));
                left -= n;
                if (left < 0) throw new InvalidDataException("Cópia grande demais.");
                return n;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    /// <summary>Onde as cópias ficam guardadas.</summary>
    public interface IBackupStore
    {
        Task<List<BackupInfo>> ListAsync(CancellationToken ct = default);
        Task<byte[]> GetAsync(string name, CancellationToken ct = default);
        Task SaveAsync(BackupInfo info, byte[] data, CancellationToken ct = default);
        /// <summary>Apaga as cópias criadas antes de <paramref name="olderThanUtc"/>. Devolve quantas apagou.</summary>
        Task<int> PurgeAsync(DateTime olderThanUtc, CancellationToken ct = default);
    }

    public sealed class MemoryBackupStore : IBackupStore
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, (BackupInfo Info, byte[] Data)> items = new Dictionary<string, (BackupInfo, byte[])>();

        public Task<List<BackupInfo>> ListAsync(CancellationToken ct = default)
        {
            lock (gate) return Task.FromResult(items.Values.Select(v => v.Info).OrderByDescending(i => i.CreatedAt).ToList());
        }

        public Task<byte[]> GetAsync(string name, CancellationToken ct = default)
        {
            lock (gate) return Task.FromResult(name != null && items.TryGetValue(name, out var v) ? v.Data : null);
        }

        public Task SaveAsync(BackupInfo info, byte[] data, CancellationToken ct = default)
        {
            lock (gate) items[info.Name] = (info, data);
            return Task.CompletedTask;
        }

        public Task<int> PurgeAsync(DateTime olderThanUtc, CancellationToken ct = default)
        {
            lock (gate)
            {
                var old = items.Values.Where(v => v.Info.CreatedAt < olderThanUtc).Select(v => v.Info.Name).ToList();
                foreach (var n in old) items.Remove(n);
                return Task.FromResult(old.Count);
            }
        }
    }

    /// <summary>Cópias na tabela account_backups do mesmo Postgres (compactadas).</summary>
    public sealed class PostgresBackupStore : IBackupStore
    {
        private readonly string connectionString;
        private bool ensured;

        public PostgresBackupStore(string databaseUrl)
        {
            connectionString = PostgresAccountStore.ToConnectionString(databaseUrl);
        }

        private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
        {
            var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);
            if (!ensured)
            {
                await using var cmd = new NpgsqlCommand(@"
create table if not exists account_backups (
  name text primary key, created_at timestamptz not null, accounts integer not null, data bytea not null);", conn);
                await cmd.ExecuteNonQueryAsync(ct);
                ensured = true;
            }
            return conn;
        }

        public async Task<List<BackupInfo>> ListAsync(CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                "select name, created_at, accounts, octet_length(data) from account_backups order by created_at desc", conn);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var list = new List<BackupInfo>();
            while (await r.ReadAsync(ct))
                list.Add(new BackupInfo { Name = r.GetString(0), CreatedAt = r.GetDateTime(1), Accounts = r.GetInt32(2), Bytes = r.GetInt32(3) });
            return list;
        }

        public async Task<byte[]> GetAsync(string name, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("select data from account_backups where name = @n", conn);
            cmd.Parameters.AddWithValue("n", name ?? "");
            return (byte[])await cmd.ExecuteScalarAsync(ct);
        }

        public async Task SaveAsync(BackupInfo info, byte[] data, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(@"
insert into account_backups (name, created_at, accounts, data) values (@n, @c, @a, @d)
on conflict (name) do update set created_at = excluded.created_at, accounts = excluded.accounts, data = excluded.data;", conn);
            cmd.Parameters.AddWithValue("n", info.Name);
            cmd.Parameters.AddWithValue("c", DateTime.SpecifyKind(info.CreatedAt, DateTimeKind.Utc));
            cmd.Parameters.AddWithValue("a", info.Accounts);
            cmd.Parameters.AddWithValue("d", data);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<int> PurgeAsync(DateTime olderThanUtc, CancellationToken ct = default)
        {
            await using var conn = await OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("delete from account_backups where created_at < @t", conn);
            cmd.Parameters.AddWithValue("t", DateTime.SpecifyKind(olderThanUtc, DateTimeKind.Utc));
            return await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>
    /// Cópia de segurança automática: uma por dia (horário de Brasília), guardada no banco por
    /// <see cref="KeepDays"/> dias. O painel também baixa uma cópia na hora e restaura.
    /// </summary>
    public sealed class BackupService : BackgroundService
    {
        public const int KeepDays = 14;
        public static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(1);
        public static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(30);

        private readonly GameHost host;
        private readonly IBackupStore store;
        private readonly ILogger<BackupService> logger;
        private readonly Func<DateTime> clock;
        private readonly SemaphoreSlim busy = new SemaphoreSlim(1, 1);

        public BackupService(GameHost host, IBackupStore store, ILogger<BackupService> logger, Func<DateTime> utcClock = null)
        {
            this.host = host;
            this.store = store;
            this.logger = logger;
            clock = utcClock ?? (() => DateTime.UtcNow);
        }

        public IBackupStore Store => store;

        /// <summary>Último erro da cópia automática (aparece no painel). Null = tudo certo.</summary>
        public string LastError { get; private set; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(FirstCheck, stoppingToken); }
            catch (OperationCanceledException) { return; }
            while (!stoppingToken.IsCancellationRequested)
            {
                await EnsureTodayAsync(stoppingToken);
                try { await Task.Delay(CheckEvery, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }

        /// <summary>Faz a cópia de hoje se ainda não existir. Devolve o nome da cópia feita (ou null).</summary>
        public async Task<string> EnsureTodayAsync(CancellationToken ct = default)
        {
            try
            {
                string name = StatsBook.DayOf(clock()).ToString("yyyy-MM-dd");
                var list = await store.ListAsync(ct);
                if (list.Any(b => b.Name == name)) return null;
                await TakeAsync(name, ct);
                LastError = null;
                return name;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception e)
            {
                LastError = e.Message;
                logger.LogError(e, "Falha na cópia de segurança diária");
                return null;
            }
        }

        /// <summary>Cópia das contas como estão AGORA (para baixar).</summary>
        public async Task<(byte[] Data, int Accounts)> CurrentAsync()
        {
            var accounts = await host.Query(core => core.Accounts.All());
            return (BackupFile.Write(accounts, clock()), accounts.Count);
        }

        /// <summary>Faz e guarda uma cópia com este nome; apaga as mais velhas que <see cref="KeepDays"/> dias.</summary>
        public async Task<BackupInfo> TakeAsync(string name, CancellationToken ct = default)
        {
            await busy.WaitAsync(ct);
            try
            {
                var (data, count) = await CurrentAsync();
                var info = new BackupInfo { Name = name, CreatedAt = clock(), Accounts = count, Bytes = data.Length };
                await store.SaveAsync(info, data, ct);
                int purged = await store.PurgeAsync(clock().AddDays(-KeepDays), ct);
                logger.LogInformation("Cópia de segurança {Name}: {Count} contas, {Bytes} bytes ({Purged} antigas apagadas)",
                                      name, count, data.Length, purged);
                return info;
            }
            finally
            {
                busy.Release();
            }
        }

        /// <summary>
        /// Volta as contas para o estado da cópia (contas criadas depois dela continuam como estão).
        /// Antes, guarda uma cópia de como estava (para desfazer, se precisar).
        /// </summary>
        public async Task<RestoreResult> RestoreAsync(byte[] backup, CancellationToken ct = default)
        {
            var accounts = BackupFile.Read(backup); // confere tudo antes de mexer em qualquer conta
            var now = StatsBook.DayOf(clock()).ToString("yyyy-MM-dd") + "_antes-de-restaurar_" + clock().AddHours(StatsBook.UtcOffsetHours).ToString("HHmmss");
            var safety = await TakeAsync(now, ct);
            var result = await host.Query(core => core.RestoreAccounts(accounts));
            result.SafetyBackup = safety.Name;
            logger.LogWarning("Contas restauradas: {Result} (cópia de antes: {Safety})", result.ToString(), safety.Name);
            return result;
        }
    }
}
