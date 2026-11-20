using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Fodinha.Core;
using Fodinha.Game;
using Fodinha.Game.Menu;
using Fodinha.Online;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace Fodinha.PlayMode.Tests
{
    /// <summary>
    /// Ponta a ponta: sobe o servidor .NET de verdade (Server/Fodinha.Server, já compilado), cria a sala pelo menu,
    /// adiciona bots no lobby, inicia e joga a partida inteira pela rede até o fim.
    /// Ignorado se o servidor não estiver compilado (rode: dotnet build Server/Fodinha.Server).
    /// </summary>
    [Category("Servidor")]
    public class OnlineEndToEndTests
    {
        private Process server;
        private string previousUrl;

        [TearDown]
        public void TearDown()
        {
            if (OnlineClient.Exists) OnlineClient.Instance.Disconnect();
            GamePrefs.ServerUrl = previousUrl ?? "";
            if (server != null && !server.HasExited)
            {
                try { server.Kill(); } catch { /* já saiu */ }
            }
            server = null;
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, float timeout, string what)
        {
            float t = 0f;
            while (!condition() && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(condition(), Is.True, "Tempo esgotado: " + what);
        }

        private static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        [UnityTest]
        public IEnumerator CriarSala_Bots_PartidaInteira_PelaRede()
        {
            string dll = Path.GetFullPath(Path.Combine(Application.dataPath, "../Server/Fodinha.Server/bin/Debug/net8.0/Fodinha.Server.dll"));
            if (!File.Exists(dll)) Assert.Ignore("Servidor não compilado: " + dll);
            string dotnet = File.Exists(@"C:\Program Files\dotnet\dotnet.exe") ? @"C:\Program Files\dotnet\dotnet.exe" : "dotnet";

            int port = FreePort();
            var psi = new ProcessStartInfo(dotnet, "\"" + dll + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(dll)
            };
            psi.EnvironmentVariables["PORT"] = port.ToString();
            psi.EnvironmentVariables["FODINHA_PACING"] = "instant";
            server = Process.Start(psi);

            // Espera o servidor responder.
            bool up = false;
            for (int i = 0; i < 60 && !up; i++)
            {
                using (var req = UnityWebRequest.Get("http://127.0.0.1:" + port + "/health"))
                {
                    yield return req.SendWebRequest();
                    up = req.result == UnityWebRequest.Result.Success;
                }
                if (!up) yield return new WaitForSecondsRealtime(0.5f);
            }
            Assert.That(up, Is.True, "O servidor não subiu.");

            previousUrl = GamePrefs.HasCustomServer ? GamePrefs.ServerUrl : "";
            GamePrefs.ServerUrl = "ws://127.0.0.1:" + port + "/ws";

            // Menu → criar sala
            SceneManager.LoadScene(SceneNames.Menu);
            yield return WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.Menu, 10f, "menu");
            yield return null;
            var menu = Object.FindAnyObjectByType<MenuRoot>().Screen;
            menu.ShowPage(MenuScreen.Page.CreateRoom);
            menu.RoomRules.PlayersStepper.SetIndex(2); // 4 jogadores
            menu.transform.Find("AreaSegura/Pagina_CreateRoom/Acao").GetComponent<Button>().onClick.Invoke();

            // Lobby: 2 bots e iniciar
            yield return WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.Lobby, 20f, "abrir a sala");
            yield return null;
            var lobby = Object.FindAnyObjectByType<LobbyRoot>().Screen;
            yield return WaitUntil(() => lobby.Room != null, 10f, "estado da sala");
            Assert.That(lobby.Room.IAmOwner, Is.True);
            lobby.BotButton.onClick.Invoke();
            yield return WaitUntil(() => lobby.ListedPlayers == 2, 10f, "1º bot");
            lobby.BotButton.onClick.Invoke();
            yield return WaitUntil(() => lobby.ListedPlayers == 3, 10f, "2º bot");
            lobby.StartButton.onClick.Invoke();

            // Partida pela rede
            yield return WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.OnlineMatch, 20f, "abrir a partida");
            yield return null;
            var game = Object.FindAnyObjectByType<OnlineGameRoot>();
            var session = game.Session;
            yield return WaitUntil(() => session.View != null, 10f, "primeira visão");
            Assert.That(session.View.Players.Count, Is.EqualTo(3));

            string lastKey = null;
            float elapsed = 0f;
            while (session.View.Phase != MatchPhase.MatchOver && elapsed < 180f)
            {
                var v = session.View;
                if (v.IsMyTurn)
                {
                    string key = v.RoundNumber + "/" + v.TrickNumber + "/" + v.Phase + "/" + v.MyHand.Count;
                    if (key != lastKey)
                    {
                        lastKey = key;
                        if (v.Phase == MatchPhase.Betting && v.AllowedBets.Count > 0) session.PlaceBet(v.AllowedBets[0]);
                        else if (v.Phase == MatchPhase.Playing && v.MyHand.Count > 0) session.PlayCard(v.MyHand[0]);
                    }
                }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.That(session.View.Phase, Is.EqualTo(MatchPhase.MatchOver), "A partida online não terminou.");
            yield return null;
            Assert.That(game.Screen.IsMatchOverVisible, Is.True);
            Debug.Log("[Fodinha] Partida online terminou. Vencedor: " + session.View.Players[session.View.WinnerSeat].Name);

            // "Voltar à sala" → lobby (revanche)
            yield return WaitUntil(() => OnlineClient.Instance.Room != null && !OnlineClient.Instance.Room.inMatch, 10f, "sala de volta ao lobby");
            game.Screen.transform.Find("AreaSegura/FimDePartida/Caixa/JogarDeNovo").GetComponent<Button>().onClick.Invoke();
            yield return WaitUntil(() => SceneManager.GetActiveScene().name == SceneNames.Lobby, 10f, "voltar à sala");
        }
    }
}
