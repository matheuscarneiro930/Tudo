using Fodinha.Game;
using Fodinha.Online;

namespace Fodinha.PlayMode.Tests
{
    /// <summary>
    /// Os testes nunca falam com o servidor de verdade: o menu conecta sozinho para buscar o saldo,
    /// então apontamos para um endereço sem servidor (ou para o servidor local do teste ponta a ponta).
    /// </summary>
    internal static class TestServerIsolation
    {
        public const string Nowhere = "ws://127.0.0.1:1/ws";
        private static string saved;

        public static void Begin(string url = Nowhere)
        {
            saved = GamePrefs.HasCustomServer ? GamePrefs.ServerUrl : "";
            GamePrefs.ServerUrl = url;
            if (OnlineClient.Exists) OnlineClient.Instance.Disconnect();
        }

        public static void End()
        {
            if (OnlineClient.Exists) OnlineClient.Instance.Disconnect();
            GamePrefs.ServerUrl = saved ?? "";
        }
    }
}
