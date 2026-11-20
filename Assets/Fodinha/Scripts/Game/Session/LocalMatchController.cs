using System;
using System.Collections.Generic;
using Fodinha.Core;
using Fodinha.Hosting;
using UnityEngine;

namespace Fodinha.Game
{
    /// <summary>
    /// Partida offline: o jogador do aparelho (assento 0) contra bots.
    /// Roda o <see cref="MatchEngine"/> através do <see cref="MatchDirector"/> (pausas, timer e bots)
    /// e entrega à interface apenas a visão do assento 0 — exatamente como o host fará na rede.
    /// </summary>
    public sealed class LocalMatchController : MonoBehaviour, IMatchSession
    {
        /// <summary>Configuração escolhida no menu para a próxima partida offline (null = usar a do Inspector).</summary>
        public static OfflineMatchConfig PendingConfig;

        [SerializeField] private OfflineMatchConfig config = new OfflineMatchConfig();
        [SerializeField] private PacingSettings pacing = new PacingSettings();
        [SerializeField] private bool startAutomatically = true;

        [Tooltip("Multiplica a velocidade do jogo (pausas, bots e relógio). 1 = normal.")]
        [SerializeField] private float speed = 1f;

        private MatchDirector director;
        private MatchView view;
        private int publishedVersion = -1;
        private readonly Queue<MatchEvent> pendingEvents = new Queue<MatchEvent>();

        public event Action<MatchView> ViewChanged;
        public event Action<MatchEvent> EventReceived;
        public event Action<ActionResult> ActionRejected;

        public const int HumanSeat = 0;

        public int LocalSeat => HumanSeat;
        public MatchView View => view;
        public OfflineMatchConfig Config => config;
        public bool IsRunning => director != null;

        /// <summary>Acesso de depuração/testes. A interface não deve usar.</summary>
        public MatchDirector Director => director;

        public float Speed
        {
            get => speed;
            set => speed = Mathf.Max(0f, value);
        }

        public int TimerSeat => director != null ? director.TimerSeat : -1;
        public float TurnTimeRemaining => director != null ? director.TurnTimeRemaining : 0f;
        public float TurnTimeTotal => director != null ? director.TurnTimeTotal : 1f;
        public float PhaseWaitRemaining => director != null ? director.PhaseWaitRemaining : 0f;

        private void Start()
        {
            if (!startAutomatically || director != null) return;
            var cfg = PendingConfig ?? config;
            PendingConfig = null;
            StartMatch(cfg);
        }

        /// <summary>Começa uma nova partida (descarta a atual, se houver).</summary>
        public void StartMatch(OfflineMatchConfig matchConfig, PacingSettings pacingOverride = null)
        {
            if (matchConfig == null) throw new ArgumentNullException(nameof(matchConfig));

            if (director != null) director.EventRaised -= OnDirectorEvent;

            config = matchConfig.Clone();
            config.Rules = config.Rules.WithOfficialRules(); // o jogo sempre usa as regras oficiais
            int seed = config.Seed != 0 ? config.Seed : Environment.TickCount;
            var engine = new MatchEngine(config.Rules, config.BuildSeatNames(), new SystemRandomSource(seed));
            director = new MatchDirector(engine, pacingOverride ?? pacing, new SystemRandomSource(unchecked(seed * 31 + 7)));
            for (int seat = 0; seat < engine.PlayerCount; seat++)
                director.SetBotSeat(seat, seat != HumanSeat);

            director.EventRaised += OnDirectorEvent;
            pendingEvents.Clear();
            publishedVersion = -1;
            view = null;

            director.Start();
            Publish();
        }

        /// <summary>Nova partida com a mesma configuração.</summary>
        public void Restart()
        {
            StartMatch(config);
        }

        private void Update()
        {
            if (director == null) return;
            director.Tick(Time.deltaTime * speed);
            Publish();
        }

        /// <summary>Avança a partida manualmente (testes). Equivale a um quadro com <paramref name="seconds"/> de duração.</summary>
        public void Step(float seconds)
        {
            if (director == null) return;
            director.Tick(seconds);
            Publish();
        }

        // ------------------------------------------------------------------ pedidos do jogador

        public void PlaceBet(int bet)
        {
            if (director == null) return;
            Report(director.SubmitBet(HumanSeat, bet));
        }

        public void PlayCard(Card card)
        {
            if (director == null) return;
            Report(director.SubmitPlay(HumanSeat, card));
        }

        public void ResumeControl()
        {
            if (director == null) return;
            director.RequestResume(HumanSeat);
            Publish();
        }

        /// <summary>Liga o bot no lugar do jogador (usado em testes e no modo "assistir").</summary>
        public void SetHumanAutoPlay(bool autoPlay)
        {
            if (director == null) return;
            director.Engine.SetAutoPlay(HumanSeat, autoPlay);
            Publish();
        }

        private void Report(ActionResult result)
        {
            if (result != ActionResult.Ok) ActionRejected?.Invoke(result);
            Publish();
        }

        // ------------------------------------------------------------------ publicação para a interface

        private void OnDirectorEvent(MatchEvent e)
        {
            pendingEvents.Enqueue(e);
        }

        private void Publish()
        {
            while (pendingEvents.Count > 0)
            {
                // Dequeue fora do "?.": com o evento sem ouvintes, o argumento não seria avaliado (laço infinito).
                var e = pendingEvents.Dequeue();
                EventReceived?.Invoke(e);
            }

            if (director.Version == publishedVersion) return;
            publishedVersion = director.Version;
            view = director.Engine.BuildView(HumanSeat);
            ViewChanged?.Invoke(view);
        }
    }
}
