using System.Collections.Generic;

namespace Fodinha.Hosting
{
    /// <summary>Tipo da mensagem pronta (muda a cor do botão).</summary>
    public enum QuickChatKind { Friendly, Taunt, Reaction }

    /// <summary>
    /// Mensagens prontas da partida (sem bate-papo aberto: ninguém digita nada). Pela rede vai só o NÚMERO
    /// da mensagem, então não há como mandar texto ofensivo. NUNCA mude a ordem nem apague: mensagens novas
    /// entram sempre no fim (apps antigos continuam entendendo as antigas).
    /// </summary>
    public static class QuickChat
    {
        public const int GoodPlay = 0, GotMe = 1, Thanks = 2, GoodGame = 3;
        public const int Mine = 4, Scared = 5, BeginnersLuck = 6, TakeYouWithMe = 7, Watching = 8, Cry = 9, Hurry = 10, MissedBadly = 11;
        public const int Wow = 12, Lol = 13, NoWay = 14, Ouch = 15;

        private static readonly string[] Texts =
        {
            "Boa jogada!", "Você me pegou!", "Valeu!", "Boa partida!",
            "Essa é minha!", "Tá com medo?", "Sorte de principiante...", "Vou te levar junto!",
            "Tô de olho em você", "Pode chorar!", "Anda logo!", "Errou feio!",
            "Eita!", "Kkkkkk", "Não acredito!", "Essa doeu..."
        };

        private static readonly QuickChatKind[] Kinds =
        {
            QuickChatKind.Friendly, QuickChatKind.Friendly, QuickChatKind.Friendly, QuickChatKind.Friendly,
            QuickChatKind.Taunt, QuickChatKind.Taunt, QuickChatKind.Taunt, QuickChatKind.Taunt,
            QuickChatKind.Taunt, QuickChatKind.Taunt, QuickChatKind.Taunt, QuickChatKind.Taunt,
            QuickChatKind.Reaction, QuickChatKind.Reaction, QuickChatKind.Reaction, QuickChatKind.Reaction
        };

        /// <summary>Intervalo mínimo entre duas mensagens do mesmo jogador, e o máximo por minuto.</summary>
        public const float MinIntervalSeconds = 2.5f;
        public const int MaxPerMinute = 8;

        /// <summary>Quanto tempo o balão fica na tela.</summary>
        public const float BubbleSeconds = 3.5f;

        public static int Count => Texts.Length;

        public static bool IsValid(int id) => id >= 0 && id < Texts.Length;

        public static string Text(int id) => IsValid(id) ? Texts[id] : "";

        public static QuickChatKind KindOf(int id) => IsValid(id) ? Kinds[id] : QuickChatKind.Reaction;
    }

    /// <summary>
    /// Limite de mensagens prontas de UM jogador (o servidor confere; o app usa o mesmo limite para apagar o botão).
    /// </summary>
    public sealed class QuickChatGate
    {
        /// <summary>Folga do servidor: a rede atrasa umas mensagens mais que outras (o app já segura o ritmo certo).</summary>
        public const double NetworkTolerance = 0.75;

        private readonly Queue<double> recent = new Queue<double>();
        private readonly double interval;
        private readonly double window;
        private double last = double.NegativeInfinity;

        public QuickChatGate(double tolerance = 0)
        {
            interval = QuickChat.MinIntervalSeconds - tolerance;
            window = 60.0 - tolerance;
        }

        /// <summary>Verdadeiro se pode mandar agora (e já conta esta mensagem).</summary>
        public bool TryAccept(double now)
        {
            if (now - last < interval) return false;
            Forget(now);
            if (recent.Count >= QuickChat.MaxPerMinute) return false;
            recent.Enqueue(now);
            last = now;
            return true;
        }

        /// <summary>Segundos até poder mandar de novo (0 = já pode).</summary>
        public double Wait(double now)
        {
            double wait = interval - (now - last);
            Forget(now);
            if (recent.Count >= QuickChat.MaxPerMinute) wait = System.Math.Max(wait, window - (now - recent.Peek()));
            return wait > 0 ? wait : 0;
        }

        private void Forget(double now)
        {
            while (recent.Count > 0 && now - recent.Peek() >= window) recent.Dequeue();
        }
    }
}
