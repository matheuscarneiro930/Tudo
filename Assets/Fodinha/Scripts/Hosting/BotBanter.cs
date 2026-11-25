using System;
using System.Collections.Generic;
using Fodinha.Core;

namespace Fodinha.Hosting
{
    /// <summary>
    /// Provocações dos bots com as mesmas mensagens prontas dos jogadores (<see cref="QuickChat"/>).
    /// Falam pouco de propósito (chance pequena, intervalo mínimo, um balão por vez), reagindo a lances
    /// da partida e às mensagens dos humanos. Não mexe em regra nenhuma: só lê os eventos públicos.
    /// Usado no modo offline e pelo servidor (bots das salas online).
    /// </summary>
    public sealed class BotBanter
    {
        /// <summary>Intervalo mínimo entre falas de bots por iniciativa própria; respostas a humanos esperam menos.</summary>
        public const double MinGapSeconds = 8.0;
        public const double ReplyGapSeconds = 3.0;
        public const double MinDelaySeconds = 0.8, MaxDelaySeconds = 2.0;

        private readonly MatchEngine engine;
        private readonly Func<int, bool> isBotSeat;
        private readonly IRandomSource rng;
        private double clock;
        private double lastSpokeAt = double.NegativeInfinity;
        private int pendingSeat = -1;
        private int pendingMessage = -1;
        private double pendingAt;
        private bool matchOver;

        /// <summary>Um bot falou: (assento, número da mensagem).</summary>
        public event Action<int, int> Spoke;

        /// <summary>Desligado = os bots ficam quietos.</summary>
        public bool Enabled = true;

        public BotBanter(MatchEngine engine, Func<int, bool> isBotSeat, IRandomSource rng)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.isBotSeat = isBotSeat ?? throw new ArgumentNullException(nameof(isBotSeat));
            this.rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        /// <summary>Há uma fala agendada (para testes).</summary>
        public bool HasPending => pendingSeat >= 0;

        /// <summary>Evento público da partida (do <see cref="MatchDirector"/>).</summary>
        public void OnEvent(MatchEvent e)
        {
            if (e == null || matchOver) return;
            switch (e.Type)
            {
                case MatchEventType.TrickResolved:
                    // Bot levou a rodada: às vezes comemora.
                    if (IsActiveBot(e.Seat)) Maybe(8, e.Seat, MinGapSeconds, QuickChat.Mine, QuickChat.Watching, QuickChat.Scared);
                    break;

                case MatchEventType.LifeLost:
                    if (IsBot(e.Seat)) Maybe(5, e.Seat, MinGapSeconds, QuickChat.Ouch, QuickChat.NoWay, QuickChat.Wow);
                    else Maybe(20, RandomActiveBot(e.Seat), MinGapSeconds, QuickChat.MissedBadly, QuickChat.Lol, QuickChat.Cry);
                    break;

                case MatchEventType.PlayerEliminated:
                    if (IsBot(e.Seat)) Maybe(25, e.Seat, MinGapSeconds, QuickChat.NoWay, QuickChat.Ouch);
                    else Maybe(40, RandomActiveBot(e.Seat), MinGapSeconds, QuickChat.Cry, QuickChat.Lol);
                    break;

                case MatchEventType.MatchEnded:
                    // A tela de fim cobre a mesa: os bots só respondem a quem falar com eles.
                    matchOver = true;
                    pendingSeat = -1;
                    break;
            }
        }

        /// <summary>Um humano mandou uma mensagem pronta: um bot pode responder.</summary>
        public void OnPlayerChat(int seat, int messageId)
        {
            if (IsBot(seat) || !QuickChat.IsValid(messageId)) return;
            int other = matchOver ? RandomBot(seat) : RandomActiveBot(seat);
            switch (QuickChat.KindOf(messageId))
            {
                case QuickChatKind.Taunt:
                    Maybe(45, other, ReplyGapSeconds, QuickChat.Scared, QuickChat.BeginnersLuck, QuickChat.Watching,
                          QuickChat.TakeYouWithMe, QuickChat.Lol);
                    break;
                case QuickChatKind.Friendly:
                    if (messageId == QuickChat.GoodGame) Maybe(60, other, ReplyGapSeconds, QuickChat.GoodGame);
                    else if (messageId != QuickChat.Thanks) Maybe(45, other, ReplyGapSeconds, QuickChat.Thanks);
                    break;
                default:
                    Maybe(20, other, ReplyGapSeconds, QuickChat.Lol, QuickChat.Wow);
                    break;
            }
        }

        /// <summary>Avança o relógio; solta a fala agendada quando chega a hora.</summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds > 0f) clock += deltaSeconds;
            if (pendingSeat < 0 || clock < pendingAt) return;
            int seat = pendingSeat, message = pendingMessage;
            pendingSeat = -1;
            lastSpokeAt = clock;
            if (Enabled) Spoke?.Invoke(seat, message);
        }

        private void Maybe(int percent, int seat, double gap, params int[] options)
        {
            if (!Enabled || seat < 0 || pendingSeat >= 0 || options.Length == 0) return;
            if (clock - lastSpokeAt < gap) return;
            if (rng.Next(100) >= percent) return;
            pendingSeat = seat;
            pendingMessage = options[rng.Next(options.Length)];
            pendingAt = clock + MinDelaySeconds + (MaxDelaySeconds - MinDelaySeconds) * rng.Next(1001) / 1000.0;
        }

        private bool IsBot(int seat) => seat >= 0 && seat < engine.PlayerCount && isBotSeat(seat);

        private bool IsActiveBot(int seat) => IsBot(seat) && !engine.Players[seat].IsEliminated;

        private readonly List<int> candidates = new List<int>();

        private int RandomActiveBot(int except) => Pick(except, activeOnly: true);

        private int RandomBot(int except) => Pick(except, activeOnly: false);

        private int Pick(int except, bool activeOnly)
        {
            candidates.Clear();
            for (int s = 0; s < engine.PlayerCount; s++)
                if (s != except && (activeOnly ? IsActiveBot(s) : IsBot(s))) candidates.Add(s);
            return candidates.Count == 0 ? -1 : candidates[rng.Next(candidates.Count)];
        }
    }
}
