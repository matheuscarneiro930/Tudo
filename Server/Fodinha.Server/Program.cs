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
        public static WebApplication BuildApp(string[] args, ServerOptions options = null, string urls = null, IAccountStore store = null,
                                              AdMobVerifier verifier = null)
        {
            var builder = WebApplication.CreateBuilder(args);
            string port = Environment.GetEnvironmentVariable("PORT") ?? "5080";
            builder.WebHost.UseUrls(urls ?? "http://0.0.0.0:" + port);
            // Não anunciar qual servidor web é ("Server: Kestrel"); limites de tamanho de pedido.
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.AddServerHeader = false;
                k.Limits.MaxRequestBodySize = 64 * 1024;
                k.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
                k.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
                k.Limits.MaxConcurrentConnections = 6000;
                k.Limits.MaxConcurrentUpgradedConnections = 5000;
            });

            // No Render o IP real vem da Cloudflare; fora dele esses cabeçalhos são ignorados (podem ser falsos).
            bool trustProxy = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RENDER")) ||
                              Environment.GetEnvironmentVariable("TRUST_PROXY") == "1";
            // Sites extras que podem abrir o jogo (ex.: domínio próprio). O próprio endereço do servidor sempre pode.
            var extraOrigins = (Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? "")
                .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var serverOptions = options ?? new ServerOptions();
            if (Environment.GetEnvironmentVariable("FODINHA_PACING") == "instant") serverOptions.Pacing = PacingSettings.Instant;
            // Código VIP (conta sem anúncios): só no painel do Render, nunca no código nem no app.
            string vip = Environment.GetEnvironmentVariable("VIP_CODE");
            if (!string.IsNullOrWhiteSpace(vip)) serverOptions.VipCode = vip;
            // Vídeo premiado só com o aviso assinado do Google (ligar depois de configurar a URL no AdMob).
            if (Environment.GetEnvironmentVariable("ADMOB_SSV_REQUIRED") == "1") serverOptions.RequireVerifiedAds = true;

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
            builder.Services.AddSingleton(verifier ?? new AdMobVerifier());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<GameHost>());

            var app = builder.Build();

            // Cabeçalhos de segurança em todas as respostas (site e servidor).
            app.Use(async (ctx, next) =>
            {
                var h = ctx.Response.Headers;
                h["X-Content-Type-Options"] = "nosniff";
                h["X-Frame-Options"] = "SAMEORIGIN";                       // ninguém põe o jogo dentro de outro site
                h["Content-Security-Policy"] = "frame-ancestors 'self'; base-uri 'self'; object-src 'none'; form-action 'self'";
                h["Referrer-Policy"] = "strict-origin-when-cross-origin";
                h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
                h["Cross-Origin-Opener-Policy"] = "same-origin";
                if (ctx.Request.IsHttps || ctx.Request.Headers["X-Forwarded-Proto"] == "https")
                    h["Strict-Transport-Security"] = "max-age=31536000";
                await next();
            });

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
            // Mostra a quem pergunta o PRÓPRIO IP, como o servidor o enxerga (confere os limites por IP).
            app.MapGet("/ip", (HttpContext ctx) =>
                ClientAddress.Resolve(name => ctx.Request.Headers[name], ctx.Connection.RemoteIpAddress, trustProxy));
            string storeKind = store.Kind;
            app.MapGet("/health", async (GameHost host) =>
            {
                var stats = await host.Query(core => new { rooms = core.RoomCount, players = core.PlayerCount, connected = core.ConnectedCount, accounts = core.Accounts.Count });
                return Results.Json(new { ok = true, protocol = Protocol.Version, storage = storeKind, stats.rooms, stats.players, stats.connected, stats.accounts });
            });

            // Aviso do Google (AdMob SSV): "a conta X assistiu o vídeo premiado". Só vale com assinatura válida.
            // Configurar no AdMob: Unidade de anúncio premiado → Verificação no servidor → <site>/admob/ssv
            app.MapGet("/admob/ssv", async (HttpContext ctx, GameHost host, AdMobVerifier ssv) =>
            {
                var result = await ssv.VerifyAsync(ctx.Request.QueryString.Value, ctx.RequestAborted);
                if (!result.Valid) return Results.BadRequest();
                if (!string.IsNullOrEmpty(result.UserId))
                    await host.Query(core => core.CreditVerifiedReward(result.UserId));
                return Results.Ok(); // o Google só precisa saber que recebemos
            });

            app.Map(Protocol.Path, async (HttpContext ctx, GameHost host) =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest)
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await ctx.Response.WriteAsync("Use WebSocket.");
                    return;
                }
                // Navegador de outro site tentando usar o nosso servidor: recusado. (O app de celular não manda Origin.)
                if (!OriginAllowed(ctx.Request.Headers.Origin, ctx.Request.Host.Value, extraOrigins))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                string address = ClientAddress.Resolve(name => ctx.Request.Headers[name], ctx.Connection.RemoteIpAddress, trustProxy);
                if (!host.TryEnter(address))
                {
                    ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    return;
                }
                try
                {
                    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
                    await host.HandleAsync(socket, ctx.RequestAborted, address);
                }
                finally
                {
                    host.Exit(address);
                }
            });

            return app;
        }

        /// <summary>
        /// Sem Origin (app nativo) = pode. Com Origin: só o próprio site (mesmo endereço), localhost (testes)
        /// ou os da variável ALLOWED_ORIGINS.
        /// </summary>
        public static bool OriginAllowed(string origin, string host, string[] extra)
        {
            if (string.IsNullOrEmpty(origin)) return true;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
            if (string.Equals(uri.Authority, host, StringComparison.OrdinalIgnoreCase)) return true;
            if (uri.IsLoopback) return true;
            foreach (var e in extra)
                if (string.Equals(e.TrimEnd('/'), origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
