using System;
using System.Collections.Generic;
using Fodinha.Core.Bots;

namespace Fodinha.Core
{
    /// <summary>
    /// Motor oficial da partida de Fodinha. É o ÚNICO lugar onde o estado do jogo muda.
    ///
    /// Regras de uso:
    /// - Roda apenas no host/servidor (ou localmente no modo offline).
    /// - Ações de jogadores entram por TryPlaceBet / TryPlayCard e são sempre validadas (nunca confiar no cliente).
    /// - O ritmo (pausas para animação, timer de jogada) é controlado por quem usa o motor, chamando
    ///   ContinueAfterTrick, StartNextRound e HandleTimeout no momento certo.
    /// - Cada cliente recebe apenas BuildView(seu assento), que já esconde a informação privada.
    /// </summary>
    public sealed class MatchEngine
    {
        private readonly List<PlayerState> players = new List<PlayerState>();
        private readonly IRandomSource rng;
        private readonly List<int> bettingOrder = new List<int>();
        private readonly List<PlayedCard> currentTrick = new List<PlayedCard>();
        private readonly List<TrickResult> roundTricks = new List<TrickResult>();
        private int betIndex;
        private RoundPlan currentPlan;

        /// <summary>Baralhos montados para as próximas rodadas (uso exclusivo dos testes). Se vazio, embaralha normalmente.</summary>
        internal readonly Queue<IReadOnlyList<Card>> StackedDecks = new Queue<IReadOnlyList<Card>>();

        public RuleSet Rules { get; }
        public IReadOnlyList<PlayerState> Players => players;
        public int PlayerCount => players.Count;

        public MatchPhase Phase { get; private set; } = MatchPhase.NotStarted;
        public int RoundNumber { get; private set; }
        public int CardsThisRound { get; private set; }

        /// <summary>Número da vaza atual dentro da rodada (1..CardsThisRound).</summary>
        public int TrickNumber { get; private set; }

        public int DealerSeat { get; private set; } = -1;

        /// <summary>De quem é a vez (aposta ou jogada). -1 quando ninguém deve agir.</summary>
        public int CurrentSeat { get; private set; } = -1;

        public int TrickLeaderSeat { get; private set; } = -1;

        public Card? Vira { get; private set; }
        public CardComparer Comparer { get; private set; }
        public bool IsBlindRound { get; private set; }

        public IReadOnlyList<PlayedCard> CurrentTrick => currentTrick;
        public IReadOnlyList<TrickResult> RoundTricks => roundTricks;
        public TrickResult LastTrick { get; private set; }
        public RoundSummary LastRoundSummary { get; private set; }
        public int WinnerSeat { get; private set; } = -1;

        /// <summary>Eventos públicos (sem informação privada) para UI, som e rede.</summary>
        public event Action<MatchEvent> EventRaised;

        public MatchEngine(RuleSet rules, IReadOnlyList<string> playerNames, IRandomSource rng)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (playerNames == null) throw new ArgumentNullException(nameof(playerNames));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            var errors = rules.GetValidationErrors();
            if (errors.Count > 0) throw new ArgumentException("Regras inválidas: " + string.Join(" ", errors), nameof(rules));

            if (playerNames.Count < RuleSet.MinPlayers || playerNames.Count > RuleSet.MaxPlayers)
                throw new ArgumentException("A partida precisa ter entre " + RuleSet.MinPlayers + " e " + RuleSet.MaxPlayers + " jogadores.", nameof(playerNames));

            Rules = rules.Clone();
            this.rng = rng;
            for (int seat = 0; seat < playerNames.Count; seat++)
                players.Add(new PlayerState(seat, playerNames[seat]));
        }

        // ------------------------------------------------------------------ consultas

        public int ActivePlayerCount
        {
            get
            {
                int count = 0;
                foreach (var p in players) if (p.IsActive) count++;
                return count;
            }
        }

        /// <summary>Verdadeiro quando é a vez de um jogador controlado pelo bot (ausente/desconectado/bot de verdade).</summary>
        public bool IsWaitingForBot =>
            (Phase == MatchPhase.Betting || Phase == MatchPhase.Playing) && CurrentSeat >= 0 && players[CurrentSeat].IsAutoPlay;

        /// <summary>Próximo assento vivo em sentido horário a partir de <paramref name="fromSeat"/> (exclusivo).</summary>
        public int NextActiveSeat(int fromSeat)
        {
            int n = players.Count;
            for (int i = 1; i <= n; i++)
            {
                int seat = ((fromSeat + i) % n + n) % n;
                if (players[seat].IsActive) return seat;
            }
            throw new InvalidOperationException("Não há jogadores ativos.");
        }

        /// <summary>Assentos vivos em sentido horário começando em <paramref name="startSeat"/> (inclusivo, deve estar vivo).</summary>
        public List<int> ActiveSeatsFrom(int startSeat)
        {
            var result = new List<int>();
            int n = players.Count;
            for (int i = 0; i < n; i++)
            {
                int seat = (startSeat + i) % n;
                if (players[seat].IsActive) result.Add(seat);
            }
            return result;
        }

        /// <summary>Apostas válidas para o assento, se for a vez dele apostar. Caso contrário, lista vazia.</summary>
        public IReadOnlyList<int> GetAllowedBets(int seat)
        {
            if (Phase != MatchPhase.Betting || seat != CurrentSeat) return Array.Empty<int>();

            bool isLast = betIndex == bettingOrder.Count - 1;
            int sum = 0;
            for (int i = 0; i < betIndex; i++) sum += players[bettingOrder[i]].Bet;
            return BetRules.AllowedBets(CardsThisRound, isLast, sum, Rules);
        }

        // ------------------------------------------------------------------ fluxo da partida (chamado pelo host)

        /// <summary>Inicia a partida. Se <paramref name="firstDealerSeat"/> for -1, o primeiro distribuidor é sorteado.</summary>
        public void StartMatch(int firstDealerSeat = -1)
        {
            if (Phase != MatchPhase.NotStarted) throw new InvalidOperationException("A partida já foi iniciada.");
            if (firstDealerSeat >= players.Count) throw new ArgumentOutOfRangeException(nameof(firstDealerSeat));

            foreach (var p in players)
            {
                p.Lives = Rules.StartingLives;
                p.IsEliminated = false;
                p.EliminatedAtRound = 0;
            }

            DealerSeat = firstDealerSeat >= 0 ? firstDealerSeat : rng.Next(players.Count);
            Raise(new MatchEvent(MatchEventType.MatchStarted, DealerSeat));
            BeginRound(RoundPlanner.FirstRound(), rotateDealer: false);
        }

        /// <summary>Depois de uma vaza resolvida (fase TrickComplete), segue para a próxima vaza ou pontua a rodada.</summary>
        public void ContinueAfterTrick()
        {
            if (Phase != MatchPhase.TrickComplete) throw new InvalidOperationException("Não há vaza concluída para continuar (fase atual: " + Phase + ").");

            if (TrickNumber >= CardsThisRound)
            {
                ScoreRound();
                return;
            }

            // Quem levou a vaza começa a próxima. Se ninguém levou, quem começou a anterior começa de novo.
            int nextLeader = LastTrick.HasWinner ? LastTrick.WinnerSeat : TrickLeaderSeat;
            TrickNumber++;
            TrickLeaderSeat = nextLeader;
            currentTrick.Clear();
            Phase = MatchPhase.Playing;
            SetCurrentSeat(nextLeader);
        }

        /// <summary>Depois da rodada pontuada (fase RoundComplete), distribui a próxima rodada.</summary>
        public void StartNextRound()
        {
            if (Phase != MatchPhase.RoundComplete) throw new InvalidOperationException("A rodada atual ainda não terminou (fase atual: " + Phase + ").");
            var plan = RoundPlanner.Next(currentPlan, ActivePlayerCount, Rules);
            BeginRound(plan, rotateDealer: true);
        }

        // ------------------------------------------------------------------ ações de jogadores (validadas)

        public ActionResult TryPlaceBet(int seat, int bet)
        {
            var check = ValidateTurn(seat, MatchPhase.Betting);
            if (check != ActionResult.Ok) return check;
            if (bet < 0 || bet > CardsThisRound) return ActionResult.BetOutOfRange;

            var allowed = GetAllowedBets(seat);
            if (!Contains(allowed, bet)) return ActionResult.BetForbiddenForLastBidder;

            players[seat].ConsecutiveTimeouts = 0;
            ApplyBet(seat, bet);
            return ActionResult.Ok;
        }

        public ActionResult TryPlayCard(int seat, Card card)
        {
            var check = ValidateTurn(seat, MatchPhase.Playing);
            if (check != ActionResult.Ok) return check;
            if (!players[seat].hand.Contains(card)) return ActionResult.CardNotInHand;

            players[seat].ConsecutiveTimeouts = 0;
            ApplyPlay(seat, card);
            return ActionResult.Ok;
        }

        /// <summary>
        /// O tempo do jogador da vez acabou: conta o tempo esgotado, liga o bot se passar do limite
        /// e executa uma ação automática no lugar dele.
        /// </summary>
        public ActionResult HandleTimeout(int seat)
        {
            var check = ValidateTurn(seat, Phase == MatchPhase.Betting ? MatchPhase.Betting : MatchPhase.Playing);
            if (check != ActionResult.Ok) return check;

            var p = players[seat];
            p.ConsecutiveTimeouts++;
            if (!p.IsAutoPlay && p.ConsecutiveTimeouts >= Rules.TimeoutsBeforeAutoPlay) SetAutoPlay(seat, true);
            return PerformAutoAction(seat);
        }

        /// <summary>O bot age pelo assento da vez (jogadores bot, ausentes ou desconectados).</summary>
        public ActionResult PerformAutoAction(int seat)
        {
            if (Phase != MatchPhase.Betting && Phase != MatchPhase.Playing) return ActionResult.WrongPhase;
            var check = ValidateTurn(seat, Phase);
            if (check != ActionResult.Ok) return check;

            var view = BuildView(seat);
            if (Phase == MatchPhase.Betting) ApplyBet(seat, SimpleBot.ChooseBet(view));
            else ApplyPlay(seat, SimpleBot.ChoosePlay(view));
            return ActionResult.Ok;
        }

        /// <summary>Liga/desliga o bot para um jogador (ex.: jogador tocou em "voltar a jogar").</summary>
        public void SetAutoPlay(int seat, bool autoPlay)
        {
            var p = GetPlayer(seat);
            if (!autoPlay && p.HasForfeited) return; // quem desistiu não volta
            if (p.IsAutoPlay == autoPlay) return;
            p.IsAutoPlay = autoPlay;
            if (!autoPlay) p.ConsecutiveTimeouts = 0;
            Raise(new MatchEvent(MatchEventType.AutoPlayChanged, seat, autoPlay ? 1 : 0));
        }

        public void SetConnected(int seat, bool connected)
        {
            var p = GetPlayer(seat);
            if (p.IsConnected == connected) return;
            p.IsConnected = connected;
            Raise(new MatchEvent(MatchEventType.ConnectionChanged, seat, connected ? 1 : 0));
        }

        /// <summary>Jogador saiu da partida de vez: o bot termina a rodada por ele e ele é eliminado ao final dela.</summary>
        public void Forfeit(int seat)
        {
            var p = GetPlayer(seat);
            if (p.IsEliminated || p.HasForfeited) return;
            p.HasForfeited = true;
            SetAutoPlay(seat, true);
        }

        // ------------------------------------------------------------------ visão filtrada por jogador

        /// <summary>
        /// Monta o que o assento <paramref name="viewerSeat"/> pode ver. Use -1 para espectador.
        /// É seguro enviar este objeto inteiro para o cliente daquele assento.
        /// </summary>
        public MatchView BuildView(int viewerSeat)
        {
            if (viewerSeat < MatchView.Spectator || viewerSeat >= players.Count) throw new ArgumentOutOfRangeException(nameof(viewerSeat));

            bool blindBetting = IsBlindRound && Phase == MatchPhase.Betting;
            var playerViews = new List<PlayerView>(players.Count);
            foreach (var p in players)
            {
                playerViews.Add(new PlayerView
                {
                    Seat = p.Seat,
                    Name = p.Name,
                    Lives = p.Lives,
                    IsEliminated = p.IsEliminated,
                    EliminatedAtRound = p.EliminatedAtRound,
                    Bet = p.Bet,
                    TricksWon = p.TricksWon,
                    HandCount = p.hand.Count,
                    IsConnected = p.IsConnected,
                    IsAutoPlay = p.IsAutoPlay,
                    VisibleCards = blindBetting && p.Seat != viewerSeat ? p.hand.ToArray() : Array.Empty<Card>()
                });
            }

            bool canSeeOwnHand = viewerSeat >= 0 && !IsBlindRound;

            return new MatchView
            {
                ViewerSeat = viewerSeat,
                Phase = Phase,
                RoundNumber = RoundNumber,
                CardsThisRound = CardsThisRound,
                TrickNumber = TrickNumber,
                DealerSeat = DealerSeat,
                CurrentSeat = CurrentSeat,
                IsBlindRound = IsBlindRound,
                Vira = Vira,
                Comparer = Comparer,
                Rules = Rules.Clone(),
                Players = playerViews,
                MyHand = canSeeOwnHand ? players[viewerSeat].hand.ToArray() : Array.Empty<Card>(),
                CurrentTrick = currentTrick.ToArray(),
                LastTrick = LastTrick,
                LastRoundSummary = LastRoundSummary,
                AllowedBets = viewerSeat >= 0 ? GetAllowedBets(viewerSeat) : Array.Empty<int>(),
                WinnerSeat = WinnerSeat
            };
        }

        // ------------------------------------------------------------------ internos

        private void BeginRound(RoundPlan plan, bool rotateDealer)
        {
            if (rotateDealer) DealerSeat = NextActiveSeat(DealerSeat);

            int maxCards = RoundPlanner.MaxCardsPerPlayer(ActivePlayerCount, Rules);
            currentPlan = new RoundPlan(Math.Min(plan.Cards, maxCards), plan.Direction);
            CardsThisRound = currentPlan.Cards;
            RoundNumber++;
            TrickNumber = 0;
            TrickLeaderSeat = -1;
            currentTrick.Clear();
            roundTricks.Clear();
            LastTrick = null;

            foreach (var p in players)
            {
                p.hand.Clear();
                p.Bet = PlayerState.NoBet;
                p.TricksWon = 0;
                p.LivesAtRoundStart = p.Lives;
            }

            Deck deck;
            if (StackedDecks.Count > 0)
            {
                deck = new Deck(StackedDecks.Dequeue());
            }
            else
            {
                deck = new Deck();
                deck.Shuffle(rng);
            }

            // Distribui uma carta por vez, começando pelo jogador depois do distribuidor (o distribuidor recebe por último).
            var order = ActiveSeatsFrom(NextActiveSeat(DealerSeat));
            for (int c = 0; c < CardsThisRound; c++)
                foreach (int seat in order)
                    players[seat].hand.Add(deck.Draw());

            Vira = Rules.ManilhaMode == ManilhaMode.Vira ? deck.Draw() : (Card?)null;
            Comparer = new CardComparer(Rules, Vira);
            IsBlindRound = Rules.BlindOneCardRounds && CardsThisRound == 1;

            Raise(new MatchEvent(MatchEventType.RoundStarted, DealerSeat, RoundNumber));
            Raise(new MatchEvent(MatchEventType.CardsDealt, -1, CardsThisRound));
            if (Vira.HasValue) Raise(new MatchEvent(MatchEventType.ViraRevealed, -1, 0, Vira));

            // Aposta começa depois do distribuidor; o distribuidor é o "pé" (aposta por último).
            bettingOrder.Clear();
            bettingOrder.AddRange(order);
            betIndex = 0;
            Phase = MatchPhase.Betting;
            SetCurrentSeat(bettingOrder[0]);
        }

        private void ApplyBet(int seat, int bet)
        {
            players[seat].Bet = bet;
            Raise(new MatchEvent(MatchEventType.BetPlaced, seat, bet));
            betIndex++;

            if (betIndex >= bettingOrder.Count)
            {
                Raise(new MatchEvent(MatchEventType.BettingFinished));
                StartTricks();
            }
            else
            {
                SetCurrentSeat(bettingOrder[betIndex]);
            }
        }

        private void StartTricks()
        {
            TrickNumber = 1;
            TrickLeaderSeat = NextActiveSeat(DealerSeat); // na primeira vaza o distribuidor joga por último
            currentTrick.Clear();

            if (IsBlindRound)
            {
                // Rodada cega: ninguém conhece a própria carta, então todas são reveladas juntas.
                foreach (int seat in ActiveSeatsFrom(TrickLeaderSeat))
                {
                    var p = players[seat];
                    var card = p.hand[0];
                    p.hand.RemoveAt(0);
                    currentTrick.Add(new PlayedCard(seat, card));
                    Raise(new MatchEvent(MatchEventType.CardPlayed, seat, 0, card));
                }
                ResolveCurrentTrick();
                return;
            }

            Phase = MatchPhase.Playing;
            SetCurrentSeat(TrickLeaderSeat);
        }

        private void ApplyPlay(int seat, Card card)
        {
            players[seat].hand.Remove(card);
            currentTrick.Add(new PlayedCard(seat, card));
            Raise(new MatchEvent(MatchEventType.CardPlayed, seat, 0, card));

            if (currentTrick.Count >= ActivePlayerCount) ResolveCurrentTrick();
            else SetCurrentSeat(NextActiveSeat(seat));
        }

        private void ResolveCurrentTrick()
        {
            var result = Comparer.ResolveTrick(currentTrick);
            if (result.HasWinner) players[result.WinnerSeat].TricksWon++;

            LastTrick = result;
            roundTricks.Add(result);
            Phase = MatchPhase.TrickComplete;
            CurrentSeat = -1;
            Raise(new MatchEvent(MatchEventType.TrickResolved, result.WinnerSeat, TrickNumber));
        }

        private void ScoreRound()
        {
            var participants = new List<PlayerState>();
            foreach (var p in players) if (p.IsActive) participants.Add(p);

            var lostBySeat = new Dictionary<int, int>();
            foreach (var p in participants)
            {
                int lost = p.HasForfeited ? p.Lives : BetRules.LivesLost(p.Bet, p.TricksWon, Rules);
                lostBySeat[p.Seat] = lost;
                p.Lives = Math.Max(0, p.Lives - lost);
                if (lost > 0) Raise(new MatchEvent(MatchEventType.LifeLost, p.Seat, lost));
            }

            bool anySurvivor = false;
            foreach (var p in participants) if (p.Lives > 0) { anySurvivor = true; break; }

            bool tiebreak = false;
            if (!anySurvivor)
            {
                // Todos os vivos zeraram juntos: vence quem tinha mais vidas antes da rodada.
                // Se houver empate, os empatados voltam com 1 vida e o jogo continua.
                var candidates = new List<PlayerState>();
                foreach (var p in participants) if (!p.HasForfeited) candidates.Add(p);
                if (candidates.Count == 0) candidates.AddRange(participants);

                int maxBefore = int.MinValue;
                foreach (var p in candidates) maxBefore = Math.Max(maxBefore, p.LivesAtRoundStart);

                int revived = 0;
                foreach (var p in candidates)
                {
                    if (p.LivesAtRoundStart != maxBefore) continue;
                    p.Lives = 1;
                    revived++;
                }
                tiebreak = true;
                Raise(new MatchEvent(MatchEventType.PlayersRevived, -1, revived));
            }

            var results = new List<PlayerRoundResult>(participants.Count);
            foreach (var p in participants)
            {
                if (p.Lives <= 0)
                {
                    p.Lives = 0;
                    p.IsEliminated = true;
                    p.EliminatedAtRound = RoundNumber;
                    p.hand.Clear();
                    Raise(new MatchEvent(MatchEventType.PlayerEliminated, p.Seat, RoundNumber));
                }
                results.Add(new PlayerRoundResult(p.Seat, p.Bet, p.TricksWon, lostBySeat[p.Seat], p.Lives, p.IsEliminated));
            }

            LastRoundSummary = new RoundSummary(RoundNumber, CardsThisRound, results, tiebreak);
            CurrentSeat = -1;
            Raise(new MatchEvent(MatchEventType.RoundScored, -1, RoundNumber));

            if (ActivePlayerCount <= 1)
            {
                foreach (var p in players) if (p.IsActive) { WinnerSeat = p.Seat; break; }
                Phase = MatchPhase.MatchOver;
                Raise(new MatchEvent(MatchEventType.MatchEnded, WinnerSeat));
            }
            else
            {
                Phase = MatchPhase.RoundComplete;
            }
        }

        private ActionResult ValidateTurn(int seat, MatchPhase expectedPhase)
        {
            if (seat < 0 || seat >= players.Count) return ActionResult.InvalidSeat;
            if (Phase != expectedPhase) return ActionResult.WrongPhase;
            if (players[seat].IsEliminated) return ActionResult.PlayerEliminated;
            if (seat != CurrentSeat) return ActionResult.NotYourTurn;
            return ActionResult.Ok;
        }

        private PlayerState GetPlayer(int seat)
        {
            if (seat < 0 || seat >= players.Count) throw new ArgumentOutOfRangeException(nameof(seat));
            return players[seat];
        }

        private void SetCurrentSeat(int seat)
        {
            CurrentSeat = seat;
            Raise(new MatchEvent(MatchEventType.TurnChanged, seat));
        }

        private void Raise(MatchEvent e)
        {
            EventRaised?.Invoke(e);
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == value) return true;
            return false;
        }
    }
}
