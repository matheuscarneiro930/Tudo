using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Fodinha.Server
{
    /// <summary>Onde as contas ficam guardadas.</summary>
    public interface IAccountStore
    {
        /// <summary>"postgres" ou "memória".</summary>
        string Kind { get; }
        Task<List<Account>> LoadAllAsync(CancellationToken ct = default);
        Task SaveAsync(Account account, CancellationToken ct = default);
    }

    /// <summary>Sem banco configurado (testes / desenvolvimento): as contas somem quando o servidor reinicia.</summary>
    public sealed class MemoryAccountStore : IAccountStore
    {
        private readonly ConcurrentDictionary<string, Account> data = new ConcurrentDictionary<string, Account>();

        public string Kind => "memória";

        public Task<List<Account>> LoadAllAsync(CancellationToken ct = default)
        {
            var list = new List<Account>();
            foreach (var a in data.Values) list.Add(a.Snapshot());
            return Task.FromResult(list);
        }

        public Task SaveAsync(Account account, CancellationToken ct = default)
        {
            data[account.Id] = account.Snapshot();
            return Task.CompletedTask;
        }
    }

    /// <summary>PostgreSQL (ex.: Neon). Configurado pela variável de ambiente DATABASE_URL.</summary>
    public sealed class PostgresAccountStore : IAccountStore
    {
        private readonly string connectionString;

        public PostgresAccountStore(string databaseUrl)
        {
            connectionString = ToConnectionString(databaseUrl);
        }

        public string Kind => "postgres";

        /// <summary>postgres://usuario:senha@host:porta/banco?sslmode=require → string de conexão do Npgsql.</summary>
        public static string ToConnectionString(string databaseUrl)
        {
            if (string.IsNullOrWhiteSpace(databaseUrl)) throw new ArgumentException("DATABASE_URL vazia.");
            if (!databaseUrl.StartsWith("postgres://") && !databaseUrl.StartsWith("postgresql://"))
                return databaseUrl; // já é uma string de conexão no formato do Npgsql

            var uri = new Uri(databaseUrl);
            var userInfo = uri.UserInfo.Split(new[] { ':' }, 2);
            var b = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 5432,
                Username = Uri.UnescapeDataString(userInfo[0]),
                Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
                Database = uri.AbsolutePath.TrimStart('/'),
                SslMode = SslMode.Require,
                Pooling = true,
                MaxPoolSize = 10
            };
            return b.ConnectionString;
        }

        private async Task EnsureTableAsync(NpgsqlConnection conn, CancellationToken ct)
        {
            const string sql = @"
create table if not exists accounts (
  id text primary key,
  token_hash text unique not null,
  name text not null,
  coins integer not null,
  games integer not null default 0,
  wins integer not null default 0,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);";
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<List<Account>> LoadAllAsync(CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);
            await EnsureTableAsync(conn, ct);
            var list = new List<Account>();
            await using var cmd = new NpgsqlCommand("select id, token_hash, name, coins, games, wins, created_at, updated_at from accounts", conn);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new Account
                {
                    Id = r.GetString(0),
                    TokenHash = r.GetString(1),
                    Name = r.GetString(2),
                    Coins = r.GetInt32(3),
                    Games = r.GetInt32(4),
                    Wins = r.GetInt32(5),
                    CreatedAt = r.GetDateTime(6),
                    UpdatedAt = r.GetDateTime(7)
                });
            }
            return list;
        }

        public async Task SaveAsync(Account a, CancellationToken ct = default)
        {
            const string sql = @"
insert into accounts (id, token_hash, name, coins, games, wins, created_at, updated_at)
values (@id, @token, @name, @coins, @games, @wins, @created, @updated)
on conflict (id) do update set name = excluded.name, coins = excluded.coins, games = excluded.games,
  wins = excluded.wins, updated_at = excluded.updated_at;";
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", a.Id);
            cmd.Parameters.AddWithValue("token", a.TokenHash);
            cmd.Parameters.AddWithValue("name", a.Name ?? "");
            cmd.Parameters.AddWithValue("coins", a.Coins);
            cmd.Parameters.AddWithValue("games", a.Games);
            cmd.Parameters.AddWithValue("wins", a.Wins);
            cmd.Parameters.AddWithValue("created", DateTime.SpecifyKind(a.CreatedAt, DateTimeKind.Utc));
            cmd.Parameters.AddWithValue("updated", DateTime.SpecifyKind(a.UpdatedAt, DateTimeKind.Utc));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>
    /// Grava as contas no banco em segundo plano (o laço do jogo nunca espera o banco).
    /// Junta várias alterações da mesma conta e tenta de novo se o banco falhar.
    /// </summary>
    public sealed class AccountWriter : BackgroundService
    {
        private readonly IAccountStore store;
        private readonly ILogger<AccountWriter> logger;
        private readonly Channel<Account> queue = Channel.CreateUnbounded<Account>(new UnboundedChannelOptions { SingleReader = true });

        public AccountWriter(IAccountStore store, ILogger<AccountWriter> logger)
        {
            this.store = store;
            this.logger = logger;
        }

        public void Enqueue(Account snapshot) => queue.Writer.TryWrite(snapshot);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var pending = new Dictionary<string, Account>();
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var first = await queue.Reader.ReadAsync(stoppingToken);
                    pending[first.Id] = first;
                    while (queue.Reader.TryRead(out var more)) pending[more.Id] = more;
                }
                catch (OperationCanceledException) { break; }
                await FlushAsync(pending, stoppingToken);
            }
            // Ao desligar: grava o que sobrou.
            while (queue.Reader.TryRead(out var rest)) pending[rest.Id] = rest;
            await FlushAsync(pending, CancellationToken.None);
        }

        private async Task FlushAsync(Dictionary<string, Account> pending, CancellationToken ct)
        {
            foreach (var a in new List<Account>(pending.Values))
            {
                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    try
                    {
                        await store.SaveAsync(a, ct);
                        pending.Remove(a.Id);
                        break;
                    }
                    catch (Exception e) when (attempt < 5)
                    {
                        logger.LogWarning(e, "Falha ao gravar conta {Id} (tentativa {Attempt})", a.Id, attempt);
                        try { await Task.Delay(500 * attempt, ct); } catch (OperationCanceledException) { return; }
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Conta {Id} não foi gravada", a.Id);
                        pending.Remove(a.Id);
                    }
                }
            }
        }
    }
}
