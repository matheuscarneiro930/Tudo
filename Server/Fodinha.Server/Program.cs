using System;
using System.Threading.Tasks;
using Fodinha.Hosting;
using Fodinha.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fodinha.Server
{
    public static class Program
    {
        public static Task Main(string[] args) => BuildApp(args).RunAsync();

        /// <summary>
        /// Carrega as contas antes de abrir o servidor. Se o banco estiver configurado e falhar, NÃO sobe
        /// (subir sem as contas criaria carteiras novas por cima das existentes). O Render tenta de novo sozinho.
        /// </summary>
        private static System.Collections.Generic.List<Account> LoadAccounts(IAccountStore store)
        {
            Exception last = null;
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    var list = store.LoadAllAsync().GetAwaiter().GetResult();
                    Console.WriteLine("[Fodinha] Contas carregadas (" + store.Kind + "): " + list.Count);
                    return list;
                }
                catch (Exception e)
                {
                    last = e;
                    Console.WriteLine("[Fodinha] Falha ao carregar contas (tentativa " + attempt + "): " + e.Message);
                    System.Threading.Thread.Sleep(2000 * attempt);
                }
            }
            throw new InvalidOperationException("Não foi possível carregar as contas do banco.", last);
        }

        /// <summary>
        /// Monta o servidor. Porta: variável PORT (padrão 5080). Para testes, <paramref name="options"/> permite
        /// trocar o ritmo (ex.: sem pausas) e <paramref name="urls"/> a porta.
        /// </summary>
        public static WebApplication BuildApp(string[] args, ServerOptions options = null, string urls = null, IAccountStore store = null)
        {
            var builder = WebApplication.CreateBuilder(args);
            string port = Environment.GetEnvironmentVariable("PORT") ?? "5080";
            builder.WebHost.UseUrls(urls ?? "http://0.0.0.0:" + port);

            var serverOptions = options ?? new ServerOptions();
            if (Environment.GetEnvironmentVariable("FODINHA_PACING") == "instant") serverOptions.Pacing = PacingSettings.Instant;
            // Código VIP (conta sem anúncios): só no painel do Render, nunca no código nem no app.
            string vip = Environment.GetEnvironmentVariable("VIP_CODE");
            if (!string.IsNullOrWhiteSpace(vip)) serverOptions.VipCode = vip;

            // Contas: Postgres (variável DATABASE_URL) ou memória (desenvolvimento/testes).
            string databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
            store ??= string.IsNullOrWhiteSpace(databaseUrl) ? new MemoryAccountStore() : new PostgresAccountStore(databaseUrl);
            var loaded = LoadAccounts(store);

            builder.Services.AddSingleton(serverOptions);
            builder.Services.AddSingleton(store);
            builder.Services.AddSingleton<AccountWriter>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<AccountWriter>());
            builder.Services.AddSingleton(sp =>
            {
                var book = new AccountBook(loaded);
                var writer = sp.GetRequiredService<AccountWriter>();
                book.Persist = writer.Enqueue;
                return new ServerCore(sp.GetRequiredService<ServerOptions>(), null, book);
            });
            builder.Services.AddSingleton<GameHost>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<GameHost>());

            var app = builder.Build();
            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

            // Versão web do jogo (wwwroot/, gerada pelo Unity): "/" abre o jogo no navegador.
            app.UseDefaultFiles();
            app.UseStaticFiles(new StaticFileOptions
            {
                ServeUnknownFileTypes = true,
                DefaultContentType = "application/octet-stream",
                OnPrepareResponse = ctx =>
                {
                    var h = WebGlFiles.For(ctx.File.Name);
                    if (h == null) return;
                    var headers = ctx.Context.Response.Headers;
                    headers["Content-Type"] = h.ContentType;
                    if (h.ContentEncoding != null) headers["Content-Encoding"] = h.ContentEncoding;
                    headers["Cache-Control"] = h.CacheControl;
                }
            });
            app.UseRouting();

            app.MapGet("/info", () => "Fodinha Online - servidor no ar. Protocolo v" + Protocol.Version);
            string storeKind = store.Kind;
            app.MapGet("/health", async (GameHost host) =>
            {
                var stats = await host.Query(core => new { rooms = core.RoomCount, players = core.PlayerCount, connected = core.ConnectedCount, accounts = core.Accounts.Count });
                return Results.Json(new { ok = true, protocol = Protocol.Version, storage = storeKind, stats.rooms, stats.players, stats.connected, stats.accounts });
            });

            app.Map(Protocol.Path, async (HttpContext ctx, GameHost host) =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest)
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await ctx.Response.WriteAsync("Use WebSocket.");
                    return;
                }
                using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
                await host.HandleAsync(socket, ctx.RequestAborted);
            });

            return app;
        }
    }
}
