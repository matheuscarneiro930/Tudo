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
        /// Monta o servidor. Porta: variável PORT (padrão 5080). Para testes, <paramref name="options"/> permite
        /// trocar o ritmo (ex.: sem pausas) e <paramref name="urls"/> a porta.
        /// </summary>
        public static WebApplication BuildApp(string[] args, ServerOptions options = null, string urls = null)
        {
            var builder = WebApplication.CreateBuilder(args);
            string port = Environment.GetEnvironmentVariable("PORT") ?? "5080";
            builder.WebHost.UseUrls(urls ?? "http://0.0.0.0:" + port);

            var serverOptions = options ?? new ServerOptions();
            if (Environment.GetEnvironmentVariable("FODINHA_PACING") == "instant") serverOptions.Pacing = PacingSettings.Instant;

            builder.Services.AddSingleton(serverOptions);
            builder.Services.AddSingleton(sp => new ServerCore(sp.GetRequiredService<ServerOptions>()));
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
            app.MapGet("/health", async (GameHost host) =>
            {
                var stats = await host.Query(core => new { rooms = core.RoomCount, players = core.PlayerCount, connected = core.ConnectedCount });
                return Results.Json(new { ok = true, protocol = Protocol.Version, stats.rooms, stats.players, stats.connected });
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
