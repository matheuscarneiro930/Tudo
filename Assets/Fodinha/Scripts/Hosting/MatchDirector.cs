using System;
using Fodinha.Core;

namespace Fodinha.Hosting
{
    /// <summary>
    /// Conduz o ritmo de uma partida no host (modo offline ou servidor da sala).
    ///
    /// NÃO contém regras: apenas decide QUANDO chamar o <see cref="MatchEngine"/>
    /// (pausas entre vazas e rodadas, tempo de pensar dos bots, tempo esgotado do jogador).
    /// Toda ação de jogador continua sendo validada pelo motor.
    ///
    /// Uso: chame <see cref="Tick"/> a cada quadro com o tempo decorrido.
    /// </summary>
    public sealed class MatchDirector
    {
        private readonly MatchEngine engine;
        private readonly PacingSettings pacing;
        private readonly IRandomSource rng;
        private readonly bool[] botSeats;

        private int turnSerial;      // muda a cada TurnChanged do motor
        private int armedSerial = -1;
        private float pendingDealDelay;
        private float graceRemaining;   // pausa antes do relógio do jogador começar a correr
        private float botRemaining;     // tempo de "pensar" do bot
        private float turnRemaining;    // relógio do jogador humano
        private float waitRemaining;    // pausa de vaza/rodada concluída

        /// <summary>Eventos do motor, repassados na mesma ordem.</summary>
        public event Action<MatchEvent> EventRaised;

        public MatchEngine Engine => engine;
        public PacingSettings Pacing => pacing;

        /// <summary>Muda sempre que o estado da partida muda (qualquer evento do motor).</summary>
        public int Version { get; private set; }

        public MatchDirector(MatchEngine engine, PacingSettings pacing, IRandomSource rng)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.pacing = (pacing ?? new PacingSettings()).Clone();
            this.rng = rng ?? throw new ArgumentNullException(nameof(rng));
            botSeats = new bool[engine.PlayerCount];
            engine.EventRaised += OnEngineEvent;
        }

        // ------------------------------------------------------------------ bots e controle

        /// <summary>Assento controlado por um bot de verdade (modo offline ou vaga preenchida por bot).</summary>
        public bool IsBotSeat(int seat) => seat >= 0 && seat < botSeats.Length && botSeats[seat];

        public void SetBotSeat(int seat, bool isBot)
        {
            if (seat < 0 || seat >= botSeats.Length) throw new ArgumentOutOfRangeException(nameof(seat));
            botSeats[seat] = isBot;
        }

        /// <summary>Verdadeiro quando quem age por este assento é o bot (bot de verdade, ausente ou desconectado).</summary>
        public bool IsAutomated(int seat)
        {
            if (seat < 0 || seat >= botSeats.Length) return false;
            var p = engine.Players[seat];
            return botSeats[seat] || p.IsAutoPlay || !p.IsConnected;
        }

        // ------------------------------------------------------------------ relógios (para a interface)

        /// <summary>Tempo total de jogada (regra da partida).</summary>
        public float TurnTimeTotal => CurrentTurnSeconds;

        /// <summary>Tempo da ação atual: apostar (BetTimeSeconds) ou jogar carta (TurnTimeSeconds).</summary>
        private int CurrentTurnSeconds => engine.Phase == MatchPhase.Betting ? engine.Rules.BetTimeSeconds : engine.Rules.TurnTimeSeconds;

        /// <summary>Assento cujo relógio está correndo, ou -1 (ninguém / bot agindo).</summary>
        public int TimerSeat
        {
            get
            {
                if (!IsActionPhase || armedSerial != turnSerial) return -1;
                int seat = engine.CurrentSeat;
                return IsAutomated(seat) ? -1 : seat;
            }
        }

        /// <summary>Segundos restantes para o jogador da vez (cheio enquanto a pausa inicial não acabou).</summary>
        public float TurnTimeRemaining => TimerSeat >= 0 ? Math.Max(0f, turnRemaining) : 0f;

        /// <summary>Segundos até o host seguir depois de uma vaza ou rodada concluída.</summary>
        public float PhaseWaitRemaining =>
            engine.Phase == MatchPhase.TrickComplete || engine.Phase == MatchPhase.RoundComplete ? Math.Max(0f, waitRemaining) : 0f;

        private bool IsActionPhase => engine.Phase == MatchPhase.Betting || engine.Phase == MatchPhase.Playing;

        // ------------------------------------------------------------------ fluxo

        public void Start(int firstDealerSeat = -1)
        {
            engine.StartMatch(firstDealerSeat);
        }

        /// <summary>Pedido de aposta vindo de um jogador (local ou rede). Bots de verdade não apostam por aqui.</summary>
        public ActionResult SubmitBet(int seat, int bet)
        {
            if (IsBotSeat(seat)) return ActionResult.NotYourTurn;
            return engine.TryPlaceBet(seat, bet);
        }

        /// <summary>Pedido de jogada vindo de um jogador (local ou rede).</summary>
        public ActionResult SubmitPlay(int seat, Card card)
        {
            if (IsBotSeat(seat)) return ActionResult.NotYourTurn;
            return engine.TryPlayCard(seat, card);
        }

        /// <summary>Jogador tocou em "voltar a jogar": desliga o bot que estava jogando por ele.</summary>
        public void RequestResume(int seat)
        {
            if (seat < 0 || seat >= botSeats.Length || botSeats[seat]) return;
            engine.SetAutoPlay(seat, false);
        }

        /// <summary>Avança o relógio. Executa no máximo uma ação do motor por chamada.</summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds < 0f) deltaSeconds = 0f;

            switch (engine.Phase)
            {
                case MatchPhase.Betting:
                case MatchPhase.Playing:
                    TickTurn(deltaSeconds);
                    break;

                case MatchPhase.TrickComplete:
                    waitRemaining -= deltaSeconds;
                    if (waitRemaining <= 0f) engine.ContinueAfterTrick();
                    break;

                case MatchPhase.RoundComplete:
                    waitRemaining -= deltaSeconds;
                    if (waitRemaining <= 0f) engine.StartNextRound();
                    break;
            }
        }

        private void TickTurn(float dt)
        {
            if (armedSerial != turnSerial) ArmTurn();

            int seat = engine.CurrentSeat;
            if (seat < 0) return;

            if (IsAutomated(seat))
            {
                botRemaining -= dt;
                if (botRemaining <= 0f) engine.PerformAutoAction(seat);
                return;
            }

            if (graceRemaining > 0f)
            {
                graceRemaining -= dt;
                return;
            }

            turnRemaining -= dt;
            if (turnRemaining <= 0f) engine.HandleTimeout(seat);
        }

        private void ArmTurn()
        {
            armedSerial = turnSerial;
            float pre = pendingDealDelay;
            pendingDealDelay = 0f;

            graceRemaining = pre;
            botRemaining = pre + RandomBetween(pacing.BotMinSeconds, pacing.BotMaxSeconds);
            turnRemaining = CurrentTurnSeconds;
        }

        private float RandomBetween(float min, float max)
        {
            if (max <= min) return Math.Max(0f, min);
            const int steps = 1000;
            return min + (max - min) * rng.Next(steps + 1) / steps;
        }

        private void OnEngineEvent(MatchEvent e)
        {
            Version++;
            switch (e.Type)
            {
                case MatchEventType.RoundStarted:
                    pendingDealDelay = pacing.DealSeconds;
                    break;
                case MatchEventType.TurnChanged:
                    turnSerial++;
                    break;
                case MatchEventType.TrickResolved:
                    waitRemaining = pacing.TrickResultSeconds + (engine.IsBlindRound ? pacing.BlindRevealExtraSeconds : 0f);
                    break;
                case MatchEventType.RoundScored:
                    waitRemaining = pacing.RoundResultSeconds;
                    break;
            }
            EventRaised?.Invoke(e);
        }
    }
}
