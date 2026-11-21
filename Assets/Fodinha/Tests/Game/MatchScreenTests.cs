using System.Collections.Generic;
using Fodinha.Core;
using Fodinha.EditorTools;
using Fodinha.Game.UI;
using Fodinha.Hosting;
using NUnit.Framework;
using UnityEngine;

namespace Fodinha.Game.Tests
{
    /// <summary>
    /// Testes da partida offline com a interface real (montada por código), sem entrar no Play Mode.
    /// </summary>
    public class MatchScreenTests
    {
        private readonly List<Object> created = new List<Object>();
        private LocalMatchController controller;
        private MatchScreen screen;

        private void Setup(int players, int seed, bool humanAuto)
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            created.Add(canvasGo);

            var logic = new GameObject("Partida");
            created.Add(logic);
            controller = logic.AddComponent<LocalMatchController>();

            screen = UiKit.Rect("Tela", canvas.transform).Fill().gameObject.AddComponent<MatchScreen>();
            var cfg = new OfflineMatchConfig { BotCount = players - 1, Seed = seed };
            controller.StartMatch(cfg, PacingSettings.Instant);
            if (humanAuto) controller.SetHumanAutoPlay(true);
            screen.Bind(controller);
        }

        [SetUp]
        public void LogStart()
        {
            Debug.Log("[Fodinha][Teste] " + TestContext.CurrentContext.Test.Name);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        [Test]
        public void PartidaCompleta_ComInterface_De2a15Jogadores([NUnit.Framework.Range(2, 15)] int players)
        {
            Setup(players, 500 + players, humanAuto: true);
            int renders = 0;
            controller.ViewChanged += _ => renders++;
            int adsBefore = Fodinha.Game.Ads.AdService.InterstitialsRequested;

            bool done = MatchDriver.AdvanceUntil(controller, v => v.Phase == MatchPhase.MatchOver, humanActs: false);

            Assert.That(done, Is.True);
            Assert.That(screen.IsMatchOverVisible, Is.True);
            Assert.That(Fodinha.Game.Ads.AdService.InterstitialsRequested, Is.EqualTo(adsBefore + 1), "Um anúncio no fim de cada partida.");
            Assert.That(renders, Is.GreaterThan(10));
            Assert.That(controller.View.WinnerSeat, Is.InRange(0, players - 1));
        }

        [Test]
        public void Aposta_PeloPainel_EhEnviadaAoMotor()
        {
            Setup(4, 42, humanAuto: false);
            Assert.That(MatchDriver.AdvanceUntil(controller, v => v.Phase == MatchPhase.Betting && v.IsMyTurn, humanActs: false), Is.True);
            Assert.That(screen.IsBetPanelVisible, Is.True);

            var view = controller.View;
            int allowed = view.AllowedBets[view.AllowedBets.Count - 1];
            var button = screen.BetPanel.Buttons[allowed];
            Assert.That(button.interactable, Is.True);
            button.onClick.Invoke();

            Assert.That(controller.View.Players[controller.LocalSeat].Bet, Is.EqualTo(allowed));
            Assert.That(screen.IsBetPanelVisible, Is.False);
        }

        [Test]
        public void ApostaProibidaDoPe_ApareceDesabilitada()
        {
            // Procura uma rodada em que o jogador local é o pé (último a apostar).
            Setup(3, 77, humanAuto: false);
            bool found = MatchDriver.AdvanceUntil(controller, v =>
            {
                if (v.Phase != MatchPhase.Betting || !v.IsMyTurn) return false;
                int bets = 0;
                foreach (var p in v.Players) if (!p.IsEliminated && p.HasBet) bets++;
                return bets == v.ActivePlayerCount - 1;
            });
            Assert.That(found, Is.True, "Não encontrou uma rodada com o jogador local sendo o pé.");

            var view = controller.View;
            int sum = 0;
            foreach (var p in view.Players) if (p.HasBet) sum += p.Bet;
            int forbidden = view.CardsThisRound - sum;
            if (forbidden >= 0 && forbidden <= view.CardsThisRound)
                Assert.That(screen.BetPanel.Buttons[forbidden].interactable, Is.False);
        }

        [Test]
        public void Jogada_DoisToquesNaCarta_JogaACarta()
        {
            Setup(3, 99, humanAuto: false);
            bool reached = MatchDriver.AdvanceUntil(controller,
                v => v.Phase == MatchPhase.Playing && v.IsMyTurn && v.MyHand.Count >= 2);
            Assert.That(reached, Is.True);

            var card = controller.View.MyHand[0];
            screen.Hand.Tap(card);
            Assert.That(screen.Hand.Selected.HasValue, Is.True, "1º toque seleciona.");
            Assert.That(controller.View.MyHand, Has.Member(card), "1º toque não joga.");

            screen.Hand.Tap(card);
            Assert.That(controller.View.MyHand, Has.No.Member(card), "2º toque joga a carta.");
        }

        [Test]
        public void RodadaCega_MaoMostraCartaDesconhecida_EAdversariosMostramACarta()
        {
            Setup(5, 123, humanAuto: false);
            Assert.That(MatchDriver.AdvanceUntil(controller, v => v.Phase == MatchPhase.Betting && v.IsBlindRound), Is.True);

            var view = controller.View;
            Assert.That(view.MyHand.Count, Is.EqualTo(0), "Na rodada cega o cliente não recebe a própria carta.");
            Assert.That(screen.Hand.VisibleCardCount, Is.EqualTo(1));
            foreach (var p in view.Players)
                if (p.Seat != view.ViewerSeat) Assert.That(p.VisibleCards.Count, Is.EqualTo(1));
        }

        [Test]
        public void ResultadoDaRodada_EhMostrado()
        {
            Setup(4, 31, humanAuto: true);
            Assert.That(MatchDriver.AdvanceUntil(controller, v => v.Phase == MatchPhase.RoundComplete, humanActs: false), Is.True);
            Assert.That(screen.IsRoundPanelVisible, Is.True);
        }

        [Test]
        public void JogarDeNovo_ReiniciaPartida()
        {
            Setup(2, 8, humanAuto: true);
            Assert.That(MatchDriver.AdvanceUntil(controller, v => v.Phase == MatchPhase.MatchOver, humanActs: false), Is.True);
            Assert.That(screen.IsMatchOverVisible, Is.True);

            controller.Restart();
            Assert.That(controller.View.Phase, Is.EqualTo(MatchPhase.Betting));
            Assert.That(controller.View.RoundNumber, Is.EqualTo(1));
            Assert.That(screen.IsMatchOverVisible, Is.False);
        }
    }

    public class TableLayoutTests
    {
        [Test]
        public void Adversarios_FicamDentroDaMesa_ESemPosicoesRepetidas([NUnit.Framework.Range(1, 15)] int opponents)
        {
            var anchors = new List<Vector2>();
            for (int rel = 1; rel <= opponents; rel++)
            {
                var a = TableLayout.SeatAnchor(TableLayout.OpponentDirection(rel, opponents));
                Assert.That(a.x, Is.InRange(0.05f, 0.95f));
                Assert.That(a.y, Is.InRange(0.05f, 0.95f));
                foreach (var other in anchors) Assert.That(Vector2.Distance(a, other), Is.GreaterThan(0.05f));
                anchors.Add(a);
            }
        }

        [Test]
        public void SentidoHorario_PrimeiroAdversarioFicaAEsquerda_UltimoADireita()
        {
            var first = TableLayout.OpponentDirection(1, 5);
            var last = TableLayout.OpponentDirection(5, 5);
            Assert.That(first.x, Is.LessThan(0f));
            Assert.That(last.x, Is.GreaterThan(0f));
        }

        [Test]
        public void IndiceRelativo_ColocaLocalEmZero()
        {
            Assert.That(TableLayout.Relative(3, 3, 6), Is.EqualTo(0));
            Assert.That(TableLayout.Relative(4, 3, 6), Is.EqualTo(1));
            Assert.That(TableLayout.Relative(2, 3, 6), Is.EqualTo(5));
        }
    }

    public class MatchRankingTests
    {
        [Test]
        public void Classificacao_VencedorPrimeiro_DepoisQuemCaiuMaisTarde()
        {
            var names = new List<string> { "A", "B", "C", "D" };
            var engine = new MatchEngine(RuleSet.CreateDefault(), names, new SystemRandomSource(5));
            var director = new MatchDirector(engine, PacingSettings.Instant, new SystemRandomSource(6));
            for (int s = 0; s < 4; s++) director.SetBotSeat(s, true);
            director.Start();
            for (int i = 0; i < 200000 && engine.Phase != MatchPhase.MatchOver; i++) director.Tick(0.02f);
            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.MatchOver));

            var view = engine.BuildView(0);
            var ranking = MatchRanking.Build(view);
            Assert.That(ranking[0].Player.Seat, Is.EqualTo(view.WinnerSeat));
            Assert.That(ranking[0].Place, Is.EqualTo(1));
            for (int i = 2; i < ranking.Count; i++)
                Assert.That(ranking[i].Player.EliminatedAtRound, Is.LessThanOrEqualTo(ranking[i - 1].Player.EliminatedAtRound));
        }
    }
}
